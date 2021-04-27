 using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Common.Models;
using Chaincase.Common.Services;
 using Microsoft.Extensions.Options;
 using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;

namespace Chaincase.UI.ViewModels
{
    public class OverviewViewModel : ReactiveObject
    {
        private readonly IMainThreadInvoker _mainThreadInvoker;
        private readonly ChaincaseWalletManager _walletManager;
        private readonly IOptions<Config> _config;
        private readonly IOptions<UiConfig> _uiConfig;
        private readonly BitcoinStore _bitcoinStore;

        private ObservableCollection<TransactionViewModel> _transactions;
        public string _balance;
        private ObservableAsPropertyHelper<bool> _hasCoins;
        private ObservableAsPropertyHelper<bool> _hasSeed;
        private ObservableAsPropertyHelper<bool> _isBackedUp;
        private ObservableAsPropertyHelper<bool> _canBackUp;
        private bool _isWalletInitialized;

        public OverviewViewModel(ChaincaseWalletManager walletManager, IOptions<Config> config, IOptions<UiConfig> uiConfig, BitcoinStore bitcoinStore, IMainThreadInvoker mainThreadInvoker)
        {
            _walletManager = walletManager;
            _config = config;
            _uiConfig = uiConfig;
            _bitcoinStore = bitcoinStore;
            _mainThreadInvoker = mainThreadInvoker;
            Transactions = new ObservableCollection<TransactionViewModel>();

            if (_walletManager.HasDefaultWalletFile() && _walletManager.CurrentWallet == null)
            {
                _walletManager.SetDefaultWallet();
                Task.Run(async () => await LoadWalletAsync());

                TryWriteTableFromCache();
            }

            _hasSeed = this.WhenAnyValue(x => x._uiConfig.Value.HasSeed)
                .ToProperty(this, nameof(HasSeed));

            _isBackedUp = this.WhenAnyValue(x => x._uiConfig.Value.IsBackedUp)
                .ToProperty(this, nameof(IsBackedUp));

            var canBackUp = this.WhenAnyValue(x => x.HasSeed, x => x.IsBackedUp,
               (hasSeed, isBackedUp) => hasSeed && !isBackedUp);

            canBackUp.ToProperty(this, x => x.CanBackUp, out _canBackUp);

            Balance = _uiConfig.Value.Balance;

            Initializing += OnInit;
            Initializing(this, EventArgs.Empty);

        }

        private event EventHandler Initializing = delegate { };

        private async void OnInit(object sender, EventArgs args)
        {
            Initializing -= OnInit;

            while (_walletManager.CurrentWallet == null || _walletManager.CurrentWallet.State < WalletState.Initialized)
            {
                await Task.Delay(200);
            }
            IsWalletInitialized = true;
            _mainThreadInvoker.Invoke(() =>
            {
                //CoinList = new CoinListViewModel();
                Observable.FromEventPattern(_walletManager.CurrentWallet.TransactionProcessor, nameof(_walletManager.CurrentWallet.TransactionProcessor.WalletRelevantTransactionProcessed))
                   .Throttle(TimeSpan.FromSeconds(0.1))
                   .ObserveOn(RxApp.MainThreadScheduler)
                   .Subscribe(_ =>
                   {
                       // TODO make ObservableAsPropertyHelper
                       Balance = _walletManager.CurrentWallet.Coins.TotalAmount().ToString();
                   });

                Observable.FromEventPattern(_walletManager.CurrentWallet, nameof(_walletManager.CurrentWallet.NewBlockProcessed))
                    .Merge(Observable.FromEventPattern(_walletManager.CurrentWallet.TransactionProcessor, nameof(_walletManager.CurrentWallet.TransactionProcessor.WalletRelevantTransactionProcessed)))
                    .Throttle(TimeSpan.FromSeconds(3))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(async _ => await TryRewriteTableAsync());

            });
        }

        private void TryWriteTableFromCache()
        {
            try
            {
                var trs = _uiConfig.Value.Transactions?.Select(ti => new TransactionViewModel(ti)) ?? new TransactionViewModel[0];
                Transactions = new ObservableCollection<TransactionViewModel>(trs.OrderByDescending(t => t.DateString));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private async Task TryRewriteTableAsync()
        {
            try
            {
                var historyBuilder = new TransactionHistoryBuilder(_walletManager.CurrentWallet);
                var txRecordList = await Task.Run(historyBuilder.BuildHistorySummary);
                var tis = txRecordList.Select(txr => new TransactionInfo
                {
                    DateTime = txr.DateTime.ToLocalTime(),
                    Confirmed = txr.Height.Type == HeightType.Chain,
                    Confirmations = txr.Height.Type == HeightType.Chain ? (int)_bitcoinStore.SmartHeaderChain.TipHeight - txr.Height.Value + 1 : 0,
                    AmountBtc = $"{txr.Amount.ToString(fplus: true, trimExcessZero: true)}",
                    Label = txr.Label,
                    BlockHeight = txr.Height.Type == HeightType.Chain ? txr.Height.Value : 0,
                    TransactionId = txr.TransactionId.ToString()
                });

                Transactions?.Clear();
                var trs = tis.Select(ti => new TransactionViewModel(ti));

                Transactions = new ObservableCollection<TransactionViewModel>(trs.OrderByDescending(t => t.DateString));

                _uiConfig.Value.Transactions = tis.ToArray();
                _uiConfig.Value.ToFile(); // write to file once height is the highest
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private async Task LoadWalletAsync()
        {
            string walletName = _config.Value.Network.ToString();
            KeyManager keyManager = _walletManager.GetWalletByName(walletName).KeyManager;
            if (keyManager is null)
            {
                return;
            }

            try
            {
                _walletManager.CurrentWallet = await _walletManager.StartWalletAsync(keyManager);
                // Successfully initialized.
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogTrace(ex);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        public bool IsBackedUp => _isBackedUp.Value;

        public bool HasSeed => _hasSeed.Value;

        public bool CanBackUp => _canBackUp.Value;

        public bool HasCoins => _hasCoins.Value;

        public bool IsWalletInitialized
        {
            get => _isWalletInitialized;
            set => this.RaiseAndSetIfChanged(ref _isWalletInitialized, value);
        }

        public string Balance
        {
            get => _balance;
            set => this.RaiseAndSetIfChanged(ref _balance, value);
        }

        public ObservableCollection<TransactionViewModel> Transactions
        {
            get => _transactions;
            set => this.RaiseAndSetIfChanged(ref _transactions, value);
        }
    }
}
