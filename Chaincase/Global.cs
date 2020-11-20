using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chaincase.Notifications;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using NBitcoin.Protocol.Connectors;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.CoinJoin.Client;
using WalletWasabi.CoinJoin.Client.Clients.Queuing;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;
using Nito.AsyncEx;
using Xamarin.Forms;


namespace Chaincase
{
    public class Global
    {
        public string DataDir { get; }
        public string TorLogsFile { get; }

        public BitcoinStore BitcoinStore { get; private set; }
        public Config Config { get; private set; }
        public UiConfig UiConfig { get; private set; }

        public string AddressManagerFilePath { get; private set; }
        public AddressManager AddressManager { get; private set; }

        public NodesGroup Nodes { get; private set; }
        public WasabiSynchronizer Synchronizer { get; private set; }
        public FeeProviders FeeProviders { get; private set; }
        public WalletManager WalletManager { get; private set; }
        public TransactionBroadcaster TransactionBroadcaster { get; set; }
        public CoinJoinProcessor CoinJoinProcessor { get; set; }
        public Node RegTestMempoolServingNode { get; private set; }
        public ITorManager TorManager { get; private set; }
        public INotificationManager NotificationManager { get; private set; }

        public HostedServices HostedServices { get; }

        public Network Network => Config.Network;

        public MemoryCache Cache { get; private set; }
        /// <summary>
        /// Responsible for keeping one life cycle event in critical sections at once
        /// </summary>
        private readonly AsyncLock LifeCycleMutex = new AsyncLock();
        private readonly AsyncLock SleepMutex = new AsyncLock();

        // Chaincase Specific
        public Wallet Wallet { get; set; }

        public Global()
        {
            using (BenchmarkLogger.Measure())
            {
                StoppingCts = new CancellationTokenSource();
                DataDir = GetDataDir();
                Directory.CreateDirectory(DataDir);
                Config = new Config(Path.Combine(DataDir, "Config.json"));
                UiConfig = new UiConfig(Path.Combine(DataDir, "UiConfig.json"));
                TorLogsFile = Path.Combine(DataDir, "TorLogs.txt");

                Logger.InitializeDefaults(Path.Combine(DataDir, "Logs.txt"));

                UiConfig.LoadOrCreateDefaultFile();
                Config.LoadOrCreateDefaultFile();

                NotificationManager = DependencyService.Get<INotificationManager>();

                WalletManager = new WalletManager(Network, new WalletDirectories(DataDir));
                WalletManager.OnDequeue += WalletManager_OnDequeue;
                WalletManager.WalletRelevantTransactionProcessed += WalletManager_WalletRelevantTransactionProcessed;

                var indexStore = new IndexStore(Network, new SmartHeaderChain());

                BitcoinStore = new BitcoinStore(
                    Path.Combine(DataDir, "BitcoinStore"), Network,
                    indexStore, new AllTransactionStore(), new MempoolService()
                );
            }
        }

        public void SetDefaultWallet()
        {
            Wallet = WalletManager.GetWalletByName(Network.ToString());
        }

        public event EventHandler Initialized = delegate { };

        public bool IsInitialized { get; private set; } = false;

        private bool InitializationCompleted { get; set; } = false;

        private bool InitializationStarted { get; set; } = false;

        private CancellationTokenSource StoppingCts { get; set; }

        public async Task InitializeNoWalletAsync()
        {
            AddressManager = null;
            TorManager = null;
            var cancel = StoppingCts.Token;
            Logger.LogDebug($"Global.InitializeNoWalletAsync(): Waiting for a lock");
            try
            {
                InitializationStarted = true;

                Logger.LogDebug($"Global.InitializeNoWalletAsync(): Got lock");

                Cache = new MemoryCache(new MemoryCacheOptions
                {
                    SizeLimit = 1_000,
                    ExpirationScanFrequency = TimeSpan.FromSeconds(30)
                });
                var bstoreInitTask = BitcoinStore.InitializeAsync();
                var addressManagerFolderPath = Path.Combine(DataDir, "AddressManager");

                AddressManagerFilePath = Path.Combine(addressManagerFolderPath, $"AddressManager{Network}.dat");
                var addrManTask = InitializeAddressManagerBehaviorAsync();

                var blocksFolderPath = Path.Combine(DataDir, $"Blocks{Network}");
                var userAgent = Constants.UserAgents.RandomElement();
                var connectionParameters = new NodeConnectionParameters { UserAgent = userAgent };

                if (Config.UseTor)
                {
                    Synchronizer = new WasabiSynchronizer(Network, BitcoinStore, () => Config.GetCurrentBackendUri(), Config.TorSocks5EndPoint);
                }
                else
                {
                    Synchronizer = new WasabiSynchronizer(Network, BitcoinStore, Config.GetFallbackBackendUri(), null);
                }
                cancel.ThrowIfCancellationRequested();

                #region TorProcessInitialization

                if (Config.UseTor)
                {
                    TorManager = DependencyService.Get<ITorManager>();
                    await TorManager.StartAsync(false, DataDir);
                }
                else
                {
                    TorManager = DependencyService.Get<ITorManager>().Mock();
                    await TorManager.StartAsync(false, DataDir);
                }

                Logger.LogInfo($"Global.InitializeNoWalletAsync():{nameof(TorManager)} is initialized.");

                #endregion TorProcessInitialization

                #region BitcoinStoreInitialization

                await bstoreInitTask.ConfigureAwait(false);

                // Make sure that the height of the wallets will not be better than the current height of the filters.
                WalletManager.SetMaxBestHeight(BitcoinStore.IndexStore.SmartHeaderChain.TipHeight);

                #endregion BitcoinStoreInitialization

                #region FeeProviderInitialization
                // Mirrors #region BitcoinCoreInitialization in WalletWasabi

                var feeProviderList = new List<IFeeProvider>
            {
                Synchronizer
            };

                FeeProviders = new FeeProviders(feeProviderList);

                #endregion FeeProviderInitialization

                #region MempoolInitialization

                connectionParameters.TemplateBehaviors.Add(BitcoinStore.CreateUntrustedP2pBehavior());

                #endregion MempoolInitialization

                #region AddressManagerInitialization

                AddressManagerBehavior addressManagerBehavior = await addrManTask.ConfigureAwait(false);
                connectionParameters.TemplateBehaviors.Add(addressManagerBehavior);

                #endregion AddressManagerInitialization

                #region P2PInitialization

                if (Network == Network.RegTest)
                {
                    Nodes = new NodesGroup(Network, requirements: Constants.NodeRequirements);
                    try
                    {
                        EndPoint bitcoinCoreEndpoint = Config.GetBitcoinP2pEndPoint();

                        Node node = await Node.ConnectAsync(NBitcoin.Network.RegTest, bitcoinCoreEndpoint).ConfigureAwait(false);

                        Nodes.ConnectedNodes.Add(node);

                        RegTestMempoolServingNode = await Node.ConnectAsync(NBitcoin.Network.RegTest, bitcoinCoreEndpoint).ConfigureAwait(false);

                        RegTestMempoolServingNode.Behaviors.Add(BitcoinStore.CreateUntrustedP2pBehavior());
                    }
                    catch (SocketException ex)
                    {
                        Logger.LogError(ex);
                    }
                }
                else
                {
                    if (Config.UseTor)
                    {
                        // onlyForOnionHosts: false - Connect to clearnet IPs through Tor, too.
                        connectionParameters.TemplateBehaviors.Add(new SocksSettingsBehavior(Config.TorSocks5EndPoint, onlyForOnionHosts: false, networkCredential: null, streamIsolation: true));
                        // allowOnlyTorEndpoints: true - Connect only to onions and don't connect to clearnet IPs at all.
                        // This of course makes the first setting unneccessary, but it's better if that's around, in case someone wants to tinker here.
                        connectionParameters.EndpointConnector = new DefaultEndpointConnector(allowOnlyTorEndpoints: Network == Network.Main);

                        await AddKnownBitcoinFullNodeAsHiddenServiceAsync(AddressManager).ConfigureAwait(false);
                    }
                    Nodes = new NodesGroup(Network, connectionParameters, requirements: Constants.NodeRequirements);
                    Nodes.MaximumNodeConnection = 12;
                    RegTestMempoolServingNode = null;
                }

                Nodes.Connect();
                Logger.LogInfo("Global.InitializeNoWalletAsync(): Start connecting to nodes...");

                var regTestMempoolServingNode = RegTestMempoolServingNode;
                if (regTestMempoolServingNode != null)
                {
                    regTestMempoolServingNode.VersionHandshake();
                    Logger.LogInfo("Global.InitializeNoWalletAsync(): Start connecting to mempool serving regtest node...");
                }

                #endregion P2PInitialization

                #region SynchronizerInitialization

                var requestInterval = TimeSpan.FromSeconds(30);
                if (Network == Network.RegTest)
                {
                    requestInterval = TimeSpan.FromSeconds(5);
                }

                int maxFiltSyncCount = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together

                Synchronizer.Start(requestInterval, TimeSpan.FromMinutes(5), maxFiltSyncCount);
                Logger.LogInfo("Global.InitializeNoWalletAsync(): Start synchronizing filters...");

                #endregion SynchronizerInitialization

                TransactionBroadcaster = new TransactionBroadcaster(Network, BitcoinStore, Synchronizer, Nodes, WalletManager, null);
                CoinJoinProcessor = new CoinJoinProcessor(Synchronizer, WalletManager, null);

                #region Blocks provider

                var blockProvider = new CachedBlockProvider(
                    new SmartBlockProvider(
                        new P2pBlockProvider(Nodes, null, Synchronizer, Config.ServiceConfiguration, Network),
                        Cache),
                    new FileSystemBlockRepository(blocksFolderPath, Network));

                #endregion Blocks provider

                WalletManager.RegisterServices(BitcoinStore, Synchronizer, Nodes, Config.ServiceConfiguration, FeeProviders, blockProvider);

                Initialized(this, EventArgs.Empty);
                IsInitialized = true;
            }
            finally
            {
                InitializationCompleted = true;
                StoppingCts = new CancellationTokenSource();
                Logger.LogDebug($"Global.InitializeNoWalletAsync(): Global.InitializeNoWalletAsync(): Finished init");

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
                    needsToDiscoverPeers = Config.UseTor == true || AddressManager.Count < 500;
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

        private IEnumerable<SmartCoin> SleepingCoins;

        private void WalletManager_OnDequeue(object? sender, DequeueResult e)
        {
            try
            {
                foreach (var success in e.Successful.Where(x => x.Value.Any()))
                {
                    DequeueReason reason = success.Key;
                    if (reason == DequeueReason.ApplicationExit)
                    {
                        SleepingCoins = success.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex);
            }
        }

        private void WalletManager_WalletRelevantTransactionProcessed(object sender, ProcessedResult e)
        {
            try
            {
                // If there are no news, then don't bother.
                if (!e.IsNews || (sender as Wallet).State != WalletState.Started)
                {
                    return;
                }

                // ToDo
                // Double spent.
                // Anonymity set gained?
                // Received dust

                bool isSpent = e.NewlySpentCoins.Any();
                bool isReceived = e.NewlyReceivedCoins.Any();
                bool isConfirmedReceive = e.NewlyConfirmedReceivedCoins.Any();
                bool isConfirmedSpent = e.NewlyConfirmedReceivedCoins.Any();
                Money miningFee = e.Transaction.Transaction.GetFee(e.SpentCoins.Select(x => x.GetCoin()).ToArray());
                if (isReceived || isSpent)
                {
                    Money receivedSum = e.NewlyReceivedCoins.Sum(x => x.Amount);
                    Money spentSum = e.NewlySpentCoins.Sum(x => x.Amount);
                    Money incoming = receivedSum - spentSum;
                    Money receiveSpentDiff = incoming.Abs();
                    string amountString = receiveSpentDiff.ToString(false, true);

                    if (e.Transaction.Transaction.IsCoinBase)
                    {
                        NotifyAndLog($"{amountString} BTC", "Mined", NotificationType.Success, e);
                    }
                    else if (isSpent && receiveSpentDiff == miningFee)
                    {
                        NotifyAndLog($"Mining Fee: {amountString} BTC", "Self Spend", NotificationType.Information, e);
                    }
                    else if (isSpent && receiveSpentDiff.Almost(Money.Zero, Money.Coins(0.01m)) && e.IsLikelyOwnCoinJoin)
                    {
                        NotifyAndLog($"CoinJoin Completed!", "", NotificationType.Success, e);
                    }
                    else if (incoming > Money.Zero)
                    {
                        if (e.Transaction.IsRBF && e.Transaction.IsReplacement)
                        {
                            NotifyAndLog($"{amountString} BTC", "Received Replaceable Replacement Transaction", NotificationType.Information, e);
                        }
                        else if (e.Transaction.IsRBF)
                        {
                            NotifyAndLog($"{amountString} BTC", "Received Replaceable Transaction", NotificationType.Success, e);
                        }
                        else if (e.Transaction.IsReplacement)
                        {
                            NotifyAndLog($"{amountString} BTC", "Received Replacement Transaction", NotificationType.Information, e);
                        }
                        else
                        {
                            NotifyAndLog($"{amountString} BTC", "Received", NotificationType.Success, e);
                        }
                    }
                    else if (incoming < Money.Zero)
                    {
                        NotifyAndLog($"{amountString} BTC", "Sent", NotificationType.Information, e);
                    }
                }
                else if (isConfirmedReceive || isConfirmedSpent)
                {
                    Money receivedSum = e.ReceivedCoins.Sum(x => x.Amount);
                    Money spentSum = e.SpentCoins.Sum(x => x.Amount);
                    Money incoming = receivedSum - spentSum;
                    Money receiveSpentDiff = incoming.Abs();
                    string amountString = receiveSpentDiff.ToString(false, true);

                    if (isConfirmedSpent && receiveSpentDiff == miningFee)
                    {
                        NotifyAndLog($"Mining Fee: {amountString} BTC", "Self Spend Confirmed", NotificationType.Information, e);
                    }
                    else if (isConfirmedSpent && e.IsLikelyOwnCoinJoin)
                    {
                        NotifyAndLog($"CoinJoin Confirmed!", "", NotificationType.Information, e);
                    }
                    else if (incoming > Money.Zero)
                    {
                        NotifyAndLog($"{amountString} BTC", "Receive Confirmed", NotificationType.Information, e);
                    }
                    else if (incoming < Money.Zero)
                    {
                        NotifyAndLog($"{amountString} BTC", "Send Confirmed", NotificationType.Information, e);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex);
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

        /// <summary>
        /// Enumeration of types for <see cref="T:Avalonia.Controls.Notifications.INotification" />.
        /// </summary>
        public enum NotificationType
        {
            Information,
            Success,
            Warning,
            Error
        }

        private void NotifyAndLog(string message, string title, NotificationType notificationType, ProcessedResult e)
        {
            message = Guard.Correct(message);
            title = Guard.Correct(title);
            // other types are best left logged for now
            if (notificationType == NotificationType.Success)
            {
                NotificationManager.ScheduleNotification(title, message, 1);
            }
            Logger.LogInfo($"Transaction Notification ({notificationType}): {title} - {message} - {e.Transaction.GetHash()}");
        }

        private protected CancellationTokenSource ResumeCts { get; set; } = new CancellationTokenSource();
        private protected bool IsResuming { get; set; } = false;

        public async Task OnResuming()
        {
			if (IsResuming)
			{
				Logger.LogDebug($"Global.OnResuming(): SleepCts.Cancel()");
				SleepCts.Cancel();
				return;
			}

			try
            {
                Logger.LogDebug($"Global.OnResuming(): Waiting for a lock");
                ResumeCts.Dispose();
                ResumeCts = new CancellationTokenSource();
                using (await LifeCycleMutex.LockAsync(ResumeCts.Token))
                {
					#region CriticalSection
					IsResuming = true;
					Logger.LogDebug($"Global.OnResuming(): Entered critical section");

					// don't ever cancel Init. use an ephemeral token
					await WaitForInitializationCompletedAsync(new CancellationToken());

					var userAgent = Constants.UserAgents.RandomElement();
					var connectionParameters = new NodeConnectionParameters { UserAgent = userAgent };
					var addrManTask = InitializeAddressManagerBehaviorAsync();
					AddressManagerBehavior addressManagerBehavior = await addrManTask.ConfigureAwait(false);
					connectionParameters.TemplateBehaviors.Add(addressManagerBehavior);

					var tor = DependencyService.Get<ITorManager>();
					await tor.StartAsync(false, GetDataDir());

					Nodes.Connect();
					Logger.LogInfo("Global.OnResuming():Start connecting to nodes...");
					//if (tor.State == TorState.Connected)
					//{   
					//    var requestInterval = (Network == Network.RegTest) ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
					//    int maxFiltSyncCount = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together
					//    Synchronizer = new WasabiSynchronizer(Network, BitcoinStore, () => Config.GetCurrentBackendUri(), Config.TorSocks5EndPoint);
					//    Synchronizer.Start(requestInterval, TimeSpan.FromMinutes(5), maxFiltSyncCount);
					//    Logger.LogInfo("Global.OnResuming():Start synchronizing filters...");
					//}
					//Synchronizer.ResponseArrived += Wallet.ChaumianClient.Synchronizer_ResponseArrivedAsync;

					//if (SleepingCoins is { })
					//{
					//	await Wallet.ChaumianClient.QueueCoinsToMixAsync(SleepingCoins);
					//	SleepingCoins = null;
					//}

					IsResuming = false;
					#endregion CriticalSection
				}
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogTrace($"Global.OnResuming(): Exception OperationCanceledException risen: {ex}");
            }
            finally
            {
                Logger.LogDebug("Global.OnResuming():Chaincase Resumed");
            }
        }

        private protected CancellationTokenSource SleepCts { get;  set; } = new CancellationTokenSource();
        private protected bool IsGoingToSleep = false;
        public async Task OnSleeping()
        {
			if (IsGoingToSleep)
			{
				Logger.LogDebug($"Global.OnResuming(): ResumeCts.Cancel()");
				ResumeCts.Cancel();
				return;
			}

			try
            {
                Logger.LogDebug($"Global.OnSleeping(): Waiting for a lock");
                SleepCts.Dispose();
                SleepCts = new CancellationTokenSource();
                using (await LifeCycleMutex.LockAsync(SleepCts.Token))
                {
					#region CriticalSection
					Logger.LogDebug($"Global.OnSleeping(): Entered critical section");

					IsGoingToSleep = true;

					// don't ever cancel Init. use an ephemeral token
					await WaitForInitializationCompletedAsync(new CancellationToken());

					var tor = DependencyService.Get<ITorManager>();
					await tor.StopAsync(Synchronizer);
					Logger.LogInfo($"Global.OnSleeping():{nameof(tor)} is stopped.");

					try
					{
						using var dequeueCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
						await WalletManager.DequeueAllCoinsGracefullyAsync(DequeueReason.ApplicationExit, dequeueCts.Token).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						Logger.LogError($"Error during {nameof(WalletManager.DequeueAllCoinsGracefullyAsync)}: {ex}");
					}

					//var synchronizer = Synchronizer;
					//if (synchronizer is { })

					//{
					//	await synchronizer.StopAsync();
					//	Logger.LogInfo($"Global.OnSleeping():{nameof(Synchronizer)} is stopped.");
					//}

					var addressManagerFilePath = AddressManagerFilePath;
					if (addressManagerFilePath is { })
					{
						IoHelpers.EnsureContainingDirectoryExists(addressManagerFilePath);
						var addressManager = AddressManager;
						if (addressManager is { })
						{
							addressManager.SavePeerFile(AddressManagerFilePath, Config.Network);
							Logger.LogInfo($"Global.OnSleeping():{nameof(AddressManager)} is saved to `{AddressManagerFilePath}`.");
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

						Logger.LogInfo($"Global.OnSleeping():{nameof(Nodes)} are disconnected.");
					}

					IsGoingToSleep = false;
					#endregion CriticalSection
				}
			}
            catch (Exception ex)
            {
                Logger.LogWarning($"Global.OnSleeping(): Error while sleeping: {ex}");
            }
            finally
            {
                Logger.LogDebug("Global.OnSleeping():Chaincase Sleeping"); // no real termination on iOS
            }
        }

        private string GetDataDir()
        {
            string dataDir;
            if (Device.RuntimePlatform == Device.iOS)
            {
                var library = Environment.GetFolderPath(Environment.SpecialFolder.Resources);
                var client = Path.Combine(library, "Client");
                dataDir = client;
            }
            else
            {
                dataDir = EnvironmentHelpers.GetDataDir(Path.Combine("Chaincase", "Client"));
            }
            return dataDir;
        }
    }
}
