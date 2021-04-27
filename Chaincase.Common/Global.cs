using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chaincase.Common.Contracts;
using Chaincase.Common.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.Protocol.Connectors;
using Nito.AsyncEx;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.CoinJoin.Client;
using WalletWasabi.CoinJoin.Client.Clients.Queuing;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;

namespace Chaincase.Common
{
    public class Global
    {
        private readonly IOptions<Config> _config;
        private Network Network => _config.Value.Network;
        private readonly IOptions<UiConfig> _uiConfig;
        private readonly BitcoinStore _bitcoinStore;
        private readonly ChaincaseWalletManager _walletManager;
        private readonly ITorManager _torManager;
        private readonly IDataDirProvider _dataDirProvider;
        private string DataDir => _dataDirProvider.Get();
        private CoinJoinProcessor _coinJoinProcessor;
        private readonly ChaincaseSynchronizer _synchronizer;
        public readonly FeeProviders _feeProviders;

        public string AddressManagerFilePath { get; private set; }
        public AddressManager AddressManager { get; private set; }

        public NodesGroup Nodes { get; private set; }
        public TransactionBroadcaster TransactionBroadcaster { get; set; }

        /// <summary>
        /// Responsible for keeping one life cycle event in critical sections at once
        /// </summary>
        private readonly AsyncLock LifeCycleMutex = new AsyncLock();
        private readonly AsyncLock SleepMutex = new AsyncLock();

        private bool InitializationStarted { get; set; } = false;
        public event EventHandler<AppInitializedEventArgs> Initialized = delegate { };
        public bool IsInitialized { get; private set; }
        private bool InitializationCompleted { get; set; }

        private CancellationTokenSource StoppingCts { get; set; }
        private protected CancellationTokenSource SleepCts { get; set; } = new CancellationTokenSource();
        private protected bool IsGoingToSleep = false;

        public Global(
            ITorManager torManager,
            IDataDirProvider dataDirProvider,
            IOptions<Config> config,
            IOptions<UiConfig> uiConfig,
            ChaincaseWalletManager walletManager,
            BitcoinStore bitcoinStore,
            ChaincaseSynchronizer synchronizer,
            FeeProviders feeProviders
            )
        {
            _torManager = torManager;
            _dataDirProvider = dataDirProvider;
            _config = config;
            _uiConfig = uiConfig;
            _walletManager = walletManager;
            _bitcoinStore = bitcoinStore;
            _synchronizer = synchronizer;
            _feeProviders = feeProviders;
            using (BenchmarkLogger.Measure())
            {
                StoppingCts = new CancellationTokenSource();
                Logger.InitializeDefaults(Path.Combine(DataDir, "Logs.txt"));
                _uiConfig.Value.LoadOrCreateDefaultFile();
            }
        }

        public async Task InitializeNoWalletAsync(CancellationToken cancellationToken = default)
        {
            AddressManager = null;
            Logger.LogDebug($"{nameof(Global)}.InitializeNoWalletAsync(): Waiting for a lock");
            try
            {
                InitializationStarted = true;

                Logger.LogDebug($"{nameof(Global)}.InitializeNoWalletAsync(): Got lock");

                MemoryCache Cache = new MemoryCache(new MemoryCacheOptions
                {
                    SizeLimit = 1_000,
                    ExpirationScanFrequency = TimeSpan.FromSeconds(30)
                });
                var bstoreInitTask = _bitcoinStore.InitializeAsync();
                var addressManagerFolderPath = Path.Combine(DataDir, "AddressManager");

                AddressManagerFilePath = Path.Combine(addressManagerFolderPath, $"AddressManager{Network}.dat");
                var addrManTask = InitializeAddressManagerBehaviorAsync();

                var blocksFolderPath = Path.Combine(DataDir, $"Blocks{Network}");
                var userAgent = Constants.UserAgents.RandomElement();
                var connectionParameters = new NodeConnectionParameters { UserAgent = userAgent };

                #region TorProcessInitialization

                if (_config.Value.UseTor)
                {
                    await _torManager.StartAsync(ensureRunning: false, DataDir);
                    Logger.LogInfo($"{nameof(_torManager)} is initialized.");
                }

                Logger.LogInfo($"{nameof(Global)}.InitializeNoWalletAsync():{nameof(_torManager)} is initialized.");

                #endregion TorProcessInitialization

                #region BitcoinStoreInitialization

                await bstoreInitTask.ConfigureAwait(false);

                // Make sure that the height of the wallets will not be better than the current height of the filters.
                _walletManager.SetMaxBestHeight(_bitcoinStore.IndexStore.SmartHeaderChain.TipHeight);

                #endregion BitcoinStoreInitialization

                #region MempoolInitialization

                connectionParameters.TemplateBehaviors.Add(_bitcoinStore.CreateUntrustedP2pBehavior());

                #endregion MempoolInitialization

                #region AddressManagerInitialization

                AddressManagerBehavior addressManagerBehavior = await addrManTask.ConfigureAwait(false);
                connectionParameters.TemplateBehaviors.Add(addressManagerBehavior);

                #endregion AddressManagerInitialization

                #region P2PInitialization
                Node regTestMempoolServingNode = null;
                if (Network == Network.RegTest)
                {
                    Nodes = new NodesGroup(Network, requirements: Constants.NodeRequirements);
                    try
                    {
                        EndPoint bitcoinCoreEndpoint = _config.Value.GetBitcoinP2pEndPoint();

                        Node node = await Node.ConnectAsync(NBitcoin.Network.RegTest, bitcoinCoreEndpoint).ConfigureAwait(false);

                        Nodes.ConnectedNodes.Add(node);

                        regTestMempoolServingNode = await Node.ConnectAsync(NBitcoin.Network.RegTest, bitcoinCoreEndpoint).ConfigureAwait(false);

                        regTestMempoolServingNode.Behaviors.Add(_bitcoinStore.CreateUntrustedP2pBehavior());
                    }
                    catch (SocketException ex)
                    {
                        Logger.LogError(ex);
                    }
                }
                else
                {
                    if (_config.Value.UseTor)
                    {
                        // onlyForOnionHosts: false - Connect to clearnet IPs through Tor, too.
                        connectionParameters.TemplateBehaviors.Add(new SocksSettingsBehavior(_config.Value.TorSocks5EndPoint, onlyForOnionHosts: false, networkCredential: null, streamIsolation: true));
                        // allowOnlyTorEndpoints: true - Connect only to onions and don't connect to clearnet IPs at all.
                        // This of course makes the first setting unneccessary, but it's better if that's around, in case someone wants to tinker here.
                        connectionParameters.EndpointConnector = new DefaultEndpointConnector(allowOnlyTorEndpoints: Network == Network.Main);

                        await AddKnownBitcoinFullNodeAsHiddenServiceAsync(AddressManager).ConfigureAwait(false);
                    }
                    Nodes = new NodesGroup(Network, connectionParameters, requirements: Constants.NodeRequirements);
                    Nodes.MaximumNodeConnection = 12;
                    regTestMempoolServingNode = null;
                }

                Nodes.Connect();
                Logger.LogInfo($"{nameof(Global)}.InitializeNoWalletAsync(): Start connecting to nodes...");

                if (regTestMempoolServingNode != null)
                {
                    regTestMempoolServingNode.VersionHandshake();
                    Logger.LogInfo($"{nameof(Global)}.InitializeNoWalletAsync(): Start connecting to mempool serving regtest node...");
                }

                #endregion P2PInitialization

                #region SynchronizerInitialization

                var requestInterval = TimeSpan.FromSeconds(30);
                if (Network == Network.RegTest)
                {
                    requestInterval = TimeSpan.FromSeconds(5);
                }

                int maxFiltSyncCount = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together

                _synchronizer.Start(requestInterval, TimeSpan.FromMinutes(5), maxFiltSyncCount);
                Logger.LogInfo($"{nameof(Global)}.InitializeNoWalletAsync(): Start synchronizing filters...");

                #endregion SynchronizerInitialization

                TransactionBroadcaster = new TransactionBroadcaster(Network, _bitcoinStore, _synchronizer, Nodes, _walletManager, null);
                // CoinJoinProcessor maintains an event handler to process joins.
                // We need this reference.
                _coinJoinProcessor = new CoinJoinProcessor(_synchronizer, _walletManager, null);

                #region Blocks provider

                var blockProvider = new CachedBlockProvider(
                    new SmartBlockProvider(
                        new P2pBlockProvider(Nodes, null, _synchronizer, _config.Value.ServiceConfiguration, Network),
                        Cache),
                    new FileSystemBlockRepository(blocksFolderPath, Network));

                #endregion Blocks provider

                _walletManager.RegisterServices(_bitcoinStore, _synchronizer, Nodes, _config.Value.ServiceConfiguration, _feeProviders, blockProvider);

                Initialized(this, new AppInitializedEventArgs(this));
                IsInitialized = true;
            }
            finally
            {
                InitializationCompleted = true;
                StoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Logger.LogDebug($"Initialization Completed");
            }
        }

        public async Task<AddressManagerBehavior> InitializeAddressManagerBehaviorAsync()
        {
            var needsToDiscoverPeers = true;
            if (Network == NBitcoin.Network.RegTest)
            {
                AddressManager = new AddressManager();
                Logger.LogInfo($"Fake {nameof(AddressManager)} is initialized on the RegTest.");
            }
            else
            {
                try
                {
                    AddressManager = await NBitcoinHelpers.LoadAddressManagerFromPeerFileAsync(AddressManagerFilePath).ConfigureAwait(false);

                    // Most of the times we do not need to discover new peers. Instead, we can connect to
                    // some of those that we already discovered in the past. In this case we assume that
                    // discovering new peers could be necessary if our address manager has less
                    // than 500 addresses. 500 addresses could be okay because previously we tried with
                    // 200 and only one user reported he/she was not able to connect (there could be many others,
                    // of course).
                    // On the other side, increasing this number forces users that do not need to discover more peers
                    // to spend resources (CPU/bandwidth) to discover new peers.
                    needsToDiscoverPeers = _config.Value.UseTor == true || AddressManager.Count < 500;
                    Logger.LogInfo($"Loaded {nameof(AddressManager)} from `{AddressManagerFilePath}`.");
                }
                catch (DirectoryNotFoundException ex)
                {
                    Logger.LogInfo($"{nameof(AddressManager)} did not exist at `{AddressManagerFilePath}`. Initializing new one.");
                    Logger.LogTrace(ex);
                    AddressManager = new AddressManager();
                }
                catch (FileNotFoundException ex)
                {
                    Logger.LogInfo($"{nameof(AddressManager)} did not exist at `{AddressManagerFilePath}`. Initializing new one.");
                    Logger.LogTrace(ex);
                    AddressManager = new AddressManager();
                }
                catch (OverflowException ex)
                {
                    // https://github.com/zkSNACKs/WalletWasabi/issues/712
                    Logger.LogInfo($"{nameof(AddressManager)} has thrown `{nameof(OverflowException)}`. Attempting to autocorrect.");
                    File.Delete(AddressManagerFilePath);
                    Logger.LogTrace(ex);
                    AddressManager = new AddressManager();
                    Logger.LogInfo($"{nameof(AddressManager)} autocorrection is successful.");
                }
                catch (FormatException ex)
                {
                    // https://github.com/zkSNACKs/WalletWasabi/issues/880
                    Logger.LogInfo($"{nameof(AddressManager)} has thrown `{nameof(FormatException)}`. Attempting to autocorrect.");
                    File.Delete(AddressManagerFilePath);
                    Logger.LogTrace(ex);
                    AddressManager = new AddressManager();
                    Logger.LogInfo($"{nameof(AddressManager)} autocorrection is successful.");
                }
            }

            var addressManagerBehavior = new AddressManagerBehavior(AddressManager)
            {
                Mode = needsToDiscoverPeers ? AddressManagerBehaviorMode.Discover : AddressManagerBehaviorMode.None
            };
            return addressManagerBehavior;
        }

        private async Task AddKnownBitcoinFullNodeAsHiddenServiceAsync(AddressManager addressManager)
        {
            if (Network == NBitcoin.Network.RegTest)
            {
                return;
            }

            //  curl -s https://bitnodes.21.co/api/v1/snapshots/latest/ | egrep -o '[a-z0-9]{16}\.onion:?[0-9]*' | sort -ru
            // Then filtered to include only /Satoshi:0.17.x
            var fullBaseDirectory = EnvironmentHelpers.GetFullBaseDirectory();

            var onions = await File.ReadAllLinesAsync(Path.Combine(fullBaseDirectory, "OnionSeeds", $"{Network}OnionSeeds.txt")).ConfigureAwait(false);

            onions.Shuffle();
            foreach (var onion in onions.Take(60))
            {
                if (EndPointParser.TryParse(onion, Network.DefaultPort, out var endpoint))
                {
                    await addressManager.AddAsync(endpoint).ConfigureAwait(false);
                }
            }

        }

        /// <returns>If initialization is successful, otherwise it was interrupted which means stopping was requested.</returns>
        public async Task<bool> WaitForInitializationCompletedAsync(CancellationToken cancellationToken)
        {
            while (!InitializationCompleted)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return !StoppingCts.IsCancellationRequested;
        }

        public event EventHandler Resumed;
        private protected CancellationTokenSource ResumeCts { get; set; } = new CancellationTokenSource();
        private protected bool IsResuming { get; set; } = false;

        public async Task OnResuming()
        {
            if (IsResuming)
            {
                Logger.LogDebug($"{nameof(Global)}.OnResuming(): SleepCts.Cancel()");
                SleepCts.Cancel();
                return;
            }

            try
            {
                Resumed?.Invoke(this, null);
                Logger.LogDebug($"{nameof(Global)}.OnResuming(): Waiting for a lock");
                ResumeCts.Dispose();
                ResumeCts = new CancellationTokenSource();
                using (await LifeCycleMutex.LockAsync(ResumeCts.Token))
                {
                    #region CriticalSection
                    IsResuming = true;
                    Logger.LogDebug($"{nameof(Global)}.OnResuming(): Entered critical section");

                    // don't ever cancel Init. use an ephemeral token
                    await WaitForInitializationCompletedAsync(new CancellationToken());

                    var userAgent = Constants.UserAgents.RandomElement();
                    var connectionParameters = new NodeConnectionParameters { UserAgent = userAgent };
                    var addrManTask = InitializeAddressManagerBehaviorAsync();
                    AddressManagerBehavior addressManagerBehavior = await addrManTask.ConfigureAwait(false);
                    connectionParameters.TemplateBehaviors.Add(addressManagerBehavior);

                    if (_torManager?.State != TorState.Started && _torManager.State != TorState.Connected)
                    {
                        await _torManager.StartAsync(false, DataDir);
                    }

                    Nodes.Connect();
                    Logger.LogInfo($"{nameof(Global)}.OnResuming():Start connecting to nodes...");

                    var requestInterval = (Network == Network.RegTest) ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
                    int maxFiltSyncCount = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together
                    _synchronizer.Resume(requestInterval, TimeSpan.FromMinutes(5), maxFiltSyncCount);
                    Logger.LogInfo($"{nameof(Global)}.OnResuming():Start synchronizing filters...");

                    if (_walletManager.SleepingCoins is { })
                    {
                        await _walletManager.CurrentWallet.ChaumianClient.QueueCoinsToMixAsync(_walletManager.SleepingCoins);
                        _walletManager.SleepingCoins = null;
                    }

                    IsResuming = false;
                    #endregion CriticalSection
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogTrace($"{nameof(Global)}.OnResuming(): Exception OperationCanceledException risen: {ex}");
            }
            finally
            {
                Logger.LogDebug($"{nameof(Global)}.OnResuming():Chaincase Resumed");
            }
        }

        public async Task OnSleeping()
        {
            if (IsGoingToSleep)
            {
                Logger.LogDebug($"{nameof(Global)}.OnResuming(): ResumeCts.Cancel()");
                ResumeCts.Cancel();
                return;
            }

            try
            {
                Logger.LogDebug($"{nameof(Global)}.OnSleeping(): Waiting for a lock");
                SleepCts.Dispose();
                SleepCts = new CancellationTokenSource();
                using (await LifeCycleMutex.LockAsync(SleepCts.Token))
                {
                    #region CriticalSection
                    Logger.LogDebug($"{nameof(Global)}.OnSleeping(): Entered critical section");

                    IsGoingToSleep = true;

                    // don't ever cancel Init. use an ephemeral token
                    await WaitForInitializationCompletedAsync(new CancellationToken());


                    if (_torManager?.State != TorState.Stopped) // OnionBrowser && Dispose@Global
                    {
                        await _torManager.StopAsync();
                        Logger.LogInfo($"{nameof(Global)}.OnSleeping():{nameof(_torManager)} is stopped.");
                    }

                    try
                    {
                        using var dequeueCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
                        await _walletManager.DequeueAllCoinsGracefullyAsync(DequeueReason.ApplicationExit, dequeueCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error during {nameof(_walletManager.DequeueAllCoinsGracefullyAsync)}: {ex}");
                    }

                    var synchronizer = _synchronizer;
                    if (synchronizer is { })

                    {
                        await synchronizer.SleepAsync();
                        Logger.LogInfo($"{nameof(Global)}.OnSleeping():{nameof(_synchronizer)} is sleeping.");
                    }

                    var addressManagerFilePath = AddressManagerFilePath;
                    if (addressManagerFilePath is { })
                    {
                        IoHelpers.EnsureContainingDirectoryExists(addressManagerFilePath);
                        var addressManager = AddressManager;
                        if (addressManager is { })
                        {
                            addressManager.SavePeerFile(AddressManagerFilePath, _config.Value.Network);
                            Logger.LogInfo($"{nameof(Global)}.OnSleeping():{nameof(AddressManager)} is saved to `{AddressManagerFilePath}`.");
                        }
                    }

                    var nodes = Nodes;
                    if (nodes is { })
                    {
                        nodes.Disconnect();
                        while (nodes.ConnectedNodes.Any(x => x.IsConnected))
                        {
                            await Task.Delay(50);
                        }

                        Logger.LogInfo($"{nameof(Global)}.OnSleeping():{nameof(Nodes)} are disconnected.");
                    }

                    IsGoingToSleep = false;
                    #endregion CriticalSection
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"{nameof(Global)}.OnSleeping(): Error while sleeping: {ex}");
            }
            finally
            {
                Logger.LogDebug($"{nameof(Global)}.OnSleeping():Chaincase Sleeping"); // no real termination on iOS
            }
        }
    }
}
