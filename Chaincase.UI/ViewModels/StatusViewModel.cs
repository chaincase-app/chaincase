using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Common.Models;
using Chaincase.Common.Services;
using Microsoft.Extensions.Options;
using NBitcoin.Protocol;
using ReactiveUI;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;

namespace Chaincase.UI.ViewModels
{
    public class StatusViewModel : ReactiveObject
    {
        private readonly Global _global;
        private readonly IOptions<Config> _config;
        private readonly IOptions<UiConfig> _uiConfig;
        private readonly ChaincaseWalletManager _walletManager;
        private readonly BitcoinStore _bitcoinStore;
        private SmartHeaderChain HashChain => _bitcoinStore.SmartHeaderChain;
        private readonly ChaincaseSynchronizer _synchronizer;
        private CompositeDisposable Disposables { get; } = new CompositeDisposable();
        private NodesCollection Nodes { get; set; }

        private ObservableAsPropertyHelper<double> _progressPercent;

        private UpdateStatus _updateStatus;
        private bool _updateAvailable;
        private bool _criticalUpdateAvailable;

        private bool UseTor { get; set; }
        private BackendStatus _backend;
        private TorStatus _tor;
        private int _peers;
        private int _maxFilters = -1;
        private ObservableAsPropertyHelper<int> _filtersLeft;
        private string _btcPrice;
        private ObservableAsPropertyHelper<string> _status;
        private bool _downloadingBlock;
        private StatusSet ActiveStatuses { get; }

        public StatusViewModel(Global global, ChaincaseWalletManager walletManager, IOptions<Config> config, IOptions<UiConfig> uiConfig, BitcoinStore bitcoinStore, ChaincaseSynchronizer synchronizer)
        {
            _global = global;
            _walletManager = walletManager;
            _bitcoinStore = bitcoinStore;
            _config = config;
            _uiConfig = uiConfig;
            _synchronizer = synchronizer;
            Backend = BackendStatus.NotConnected;
            Tor = TorStatus.NotRunning;
            Peers = 0;
            BtcPrice = "$0";
            ActiveStatuses = new StatusSet();
            UseTor = _config.Value.UseTor; // Do not make it dynamic, because if you change this config settings only next time will it activate.

            if (_global.IsInitialized)
            {
                OnAppInitialized(this, new AppInitializedEventArgs(_global));
            }
            else
            {
                _global.Initialized += OnAppInitialized;
            }

            _status = ActiveStatuses.WhenAnyValue(x => x.CurrentStatus)
                .Select(x => x.ToString())
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.Status)
                .DisposeWith(Disposables);

            // Set number of peers currently connected
            Peers = Tor == TorStatus.NotRunning ? 0 : Nodes.Count;
            // Subscribe to downloading block activity
            Observable.FromEventPattern<bool>(typeof(P2pBlockProvider), nameof(P2pBlockProvider.DownloadingBlockChanged))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(x => DownloadingBlock = x.EventArgs)
                .DisposeWith(Disposables);

            // Calculate progress precentage
            bool progressReset = true;
            _progressPercent = this.WhenAnyValue(x => x.ActiveStatuses.CurrentStatus, x => x.Peers)
                .Select(tup =>
                {
                    var (status, peers) = tup;
                    if (peers == 0 && progressReset)
                    {
                        progressReset = false;
                        return 0.01;
                    }

                    switch (status.Type)
                    {
                        case StatusType.Ready:
                            progressReset = true;
                            return 1;
                        case StatusType.Synchronizing:
                            return status.Percentage / 200.0 + 0.3;
                        case StatusType.Connecting:
                        default:
                            return 0.3;
                    }
                })
                .ToProperty(this, x => x.ProgressPercent);

            IDisposable walletCheckingInterval = null;
            Observable.FromEventPattern<bool>(typeof(Wallet), nameof(Wallet.InitializingChanged))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(x =>
                {
                    if (x.EventArgs)
                    {
                        TryAddStatus(StatusType.WalletLoading);

                        if (walletCheckingInterval is null)
                        {
                            walletCheckingInterval = Observable.Interval(TimeSpan.FromSeconds(1))
                               .ObserveOn(RxApp.MainThreadScheduler)
                               .Subscribe(_ =>
                               {
                                   var wallet = _walletManager.CurrentWallet;
                                   if (wallet is { })
                                   {
                                       var startingHeight = SmartHeader.GetStartingHeader(wallet.Network).Height;
                                       if (wallet.LastProcessedFilter?.Header?.Height is uint lastProcessedFilterHeight
                                            && lastProcessedFilterHeight > startingHeight
                                            && _bitcoinStore?.SmartHeaderChain?.TipHeight is uint tipHeight
                                            && tipHeight > startingHeight)
                                       {
                                           var allFilters = tipHeight - startingHeight;
                                           var processedFilters = lastProcessedFilterHeight - startingHeight;
                                           var perc = allFilters == 0 ?
                                                100
                                                : ((decimal)processedFilters / allFilters * 100);
                                           TryAddStatus(StatusType.WalletProcessingFilters, (ushort)perc);
                                       }

                                       var txProcessor = wallet.TransactionProcessor;
                                       if (txProcessor is { })
                                       {
                                           var perc = txProcessor.QueuedTxCount == 0 ?
                                                100
                                                : ((decimal)txProcessor.QueuedProcessedTxCount / txProcessor.QueuedTxCount * 100);
                                           TryAddStatus(StatusType.WalletProcessingTransactions, (ushort)perc);
                                       }
                                   }
                               }).DisposeWith(Disposables);
                        }
                    }
                    else
                    {
                        walletCheckingInterval?.Dispose();
                        walletCheckingInterval = null;
                        _uiConfig.Value.Balance = _walletManager.CurrentWallet.Coins.TotalAmount().ToString();
                        _uiConfig.Value.ToFile();
                        TryRemoveStatus(StatusType.WalletLoading, StatusType.WalletProcessingFilters, StatusType.WalletProcessingTransactions);
                    }
                })
                .DisposeWith(Disposables);

            this.WhenAnyValue(x => x.FiltersLeft, x => x.DownloadingBlock)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Subscribe(tup =>
               {
                   (int filtersLeft, bool downloadingBlock) = tup;
                   if (filtersLeft == 0 && !downloadingBlock)
                   {
                       TryRemoveStatus(StatusType.Synchronizing);
                   }
                   else
                   {
                       ushort perc = 0;
                       if (filtersLeft > 0)
                       {
                           if (_maxFilters == -1)
                           {
                               _maxFilters = filtersLeft;
                           }
                           perc = Convert.ToUInt16((double)(_maxFilters - filtersLeft) / _maxFilters * 100);
                       }

                       TryAddStatus(StatusType.Synchronizing, perc);
                   }
               });

            this.WhenAnyValue(x => x.Tor, x => x.Backend, x => x.Peers)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(tup =>
                {
                    (TorStatus tor, BackendStatus backend, int peers) = tup;

                    // The source of the p2p connection comes from if we use Core for it or the network.
                    var p2pConnected = peers >= 1;
                    if (tor == TorStatus.NotRunning || backend != BackendStatus.Connected || !p2pConnected)
                    {
                        TryAddStatus(StatusType.Connecting);
                    }
                    else
                    {
                        TryRemoveStatus(StatusType.Connecting);
                    }
                });
        }

        public void OnAppInitialized(object sender, AppInitializedEventArgs args)
        {
            var nodes = args.Nodes.ConnectedNodes;
            Nodes = nodes;

            Observable
                .Merge(Observable.FromEventPattern<NodeEventArgs>(nodes, nameof(nodes.Added)).Select(x => true)
                .Merge(Observable.FromEventPattern<NodeEventArgs>(nodes, nameof(nodes.Removed)).Select(x => true)
                .Merge(_synchronizer.WhenAnyValue(x => x.TorStatus).Select(x => true))))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => Peers = _synchronizer.TorStatus == TorStatus.NotRunning ? 0 : Nodes.Count) // Set peers to 0 if Tor is not running, because we get Tor status from backend answer so it seems to the user that peers are connected over clearnet, while they are not.
                .DisposeWith(Disposables);

            _synchronizer.WhenAnyValue(x => x.TorStatus)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(status => Tor = UseTor ? status : TorStatus.TurnedOff)
                .DisposeWith(Disposables);

            _synchronizer.WhenAnyValue(x => x.BackendStatus)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => Backend = _synchronizer.BackendStatus)
                .DisposeWith(Disposables);

            _filtersLeft = HashChain.WhenAnyValue(x => x.HashesLeft)
                .Throttle(TimeSpan.FromMilliseconds(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.FiltersLeft)
                .DisposeWith(Disposables);
            // Not used right now
            _synchronizer.WhenAnyValue(x => x.UsdExchangeRate)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(usd => BtcPrice = $"${(long)usd}")
                .DisposeWith(Disposables);
        }

        public BackendStatus Backend
        {
            get => _backend;
            set => this.RaiseAndSetIfChanged(ref _backend, value);
        }

        public TorStatus Tor
        {
            get => _tor;
            set => this.RaiseAndSetIfChanged(ref _tor, value);
        }

        public int Peers
        {
            get => _peers;
            set => this.RaiseAndSetIfChanged(ref _peers, value);
        }

        public double ProgressPercent => _progressPercent?.Value ?? 0;

        public int FiltersLeft => _filtersLeft?.Value ?? 0;

        public UpdateStatus UpdateStatus
        {
            get => _updateStatus;
            set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
        }

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
        }

        public bool CriticalUpdateAvailable
        {
            get => _criticalUpdateAvailable;
            set => this.RaiseAndSetIfChanged(ref _criticalUpdateAvailable, value);
        }

        public string BtcPrice
        {
            get => _btcPrice;
            set => this.RaiseAndSetIfChanged(ref _btcPrice, value);
        }

        public string Status => _status?.Value ?? "Loading...";

        public bool DownloadingBlock
        {
            get => _downloadingBlock;
            set => this.RaiseAndSetIfChanged(ref _downloadingBlock, value);
        }

        public void TryAddStatus(StatusType status, ushort percentage = 0)
        {
            try
            {
                ActiveStatuses.Set(new Status(status, percentage));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex);
            }
        }

        public void TryRemoveStatus(params StatusType[] statuses)
        {
            try
            {
                ActiveStatuses.Complete(statuses);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex);
            }
        }

        #region IDisposable Support

        private volatile bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Disposables?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion IDisposable Support
    }
}
