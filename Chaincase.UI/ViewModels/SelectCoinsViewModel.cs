using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Services;
using DynamicData;
using Microsoft.Extensions.Options;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Logging;
using WalletWasabi.Stores;

namespace Chaincase.UI.ViewModels
{
    public class SelectCoinsViewModel : ReactiveObject
    {
        private readonly ChaincaseWalletManager _walletManager;
        private readonly IOptions<Config> _config;
        private readonly BitcoinStore _bitcoinStore;

        private CompositeDisposable Disposables { get; set; }

        private ReadOnlyObservableCollection<CoinViewModel> _coinViewModels;
        private Money _selectedAmount;
        private bool _isCoinListLoading;
        private bool _isAnyCoinSelected;
        private int _selectedCount;
        private bool _warnCertainLink;
        private object SelectionChangedLock { get; } = new object();

        public event EventHandler<CoinViewModel> SelectionChanged;

        // public ReactiveCommand<CoinViewModel, Unit> OpenCoinDetail;

        public SelectCoinsViewModel(ChaincaseWalletManager walletManager, IOptions<Config> config, BitcoinStore bitcoinStore, bool isPrivate = false)
        {
            _walletManager = walletManager;
            _config = config;
            _bitcoinStore = bitcoinStore;
            RootList = new SourceList<CoinViewModel>();
            RootList
                .Connect()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _coinViewModels)
                .Subscribe();

            Disposables = Disposables is null ?
               new CompositeDisposable() :
               throw new NotSupportedException($"Cannot open {GetType().Name} before closing it.");

            UpdateRootList();

            Task.Run(async () =>
            {
                while (_walletManager.CurrentWallet?.TransactionProcessor == null)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }

                Observable
                    .Merge(Observable.FromEventPattern<ProcessedResult>(_walletManager.CurrentWallet.TransactionProcessor, nameof(_walletManager.CurrentWallet.TransactionProcessor.WalletRelevantTransactionProcessed)).Select(_ => Unit.Default))
                    .Throttle(TimeSpan.FromSeconds(1)) // Throttle TransactionProcessor events adds/removes. 
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ =>
                    {
                        UpdateRootList();
                    })
                    .DisposeWith(Disposables);
            });
        }

        public void SelectCoins(Func<CoinViewModel, bool> coinFilterPredicate)
        {
            foreach (var c in CoinList.ToArray())
            {
                c.IsSelected = coinFilterPredicate(c);
            }
        }

        private void UpdateRootList()
        {
            try
            {
                var actual = _walletManager.CurrentWallet.TransactionProcessor?.Coins?.ToHashSet()
                    ?? Enumerable.Empty<SmartCoin>();
                var old = RootList.Items.ToDictionary(c => c.Model, c => c);

                var coinToRemove = old.Where(c => !actual.Contains(c.Key)).ToArray();
                var coinToAdd = actual.Where(c => !old.ContainsKey(c)).ToArray();

                RootList.RemoveMany(coinToRemove.Select(kp => kp.Value));

                var newCoinViewModels = coinToAdd.Select(c => new CoinViewModel(_walletManager, _config, _bitcoinStore, c)).ToArray();
                foreach (var cvm in newCoinViewModels)
                {
                    SubscribeToCoinEvents(cvm);
                }
                RootList.AddRange(newCoinViewModels);

                foreach (var item in coinToRemove)
                {
                    item.Value.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                IsCoinListLoading = false;
            }
        }

        private void SubscribeToCoinEvents(CoinViewModel cvm)
        {
            cvm.WhenAnyValue(x => x.IsSelected)
                .Synchronize(SelectionChangedLock) // Use the same lock to ensure thread safety.
                .Throttle(TimeSpan.FromMilliseconds(100))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(x =>
                {
                    try
                    {
                        var coins = RootList.Items.ToArray();

                        var selectedCoins = coins.Where(x => x.IsSelected).ToArray();

                        SelectedAmount = selectedCoins.Sum(x => x.Amount);
                        IsAnyCoinSelected = selectedCoins.Any();
                        SelectedCount = selectedCoins.Count();

                        WarnCertainLink = selectedCoins
                                .Any(c => c.AnonymitySet == 1);

                        SelectionChanged?.Invoke(this, null);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex);
                    }
                })
                .DisposeWith(cvm.GetDisposables()); // Subscription will be disposed with the coinViewModel.
        }

        public bool GetCheckBoxesSelectedState(CoinViewModel[] allCoins, Func<CoinViewModel, bool> coinFilterPredicate)
        {
            var coins = allCoins.Where(coinFilterPredicate).ToArray();

            bool isAllSelected = coins.All(coin => coin.IsSelected);
            bool isAllDeselected = coins.All(coin => !coin.IsSelected);

            if (isAllDeselected)
            {
                return false;
            }

            if (isAllSelected)
            {
                if (coins.Length != allCoins.Count(coin => coin.IsSelected))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        public SourceList<CoinViewModel> RootList { get; private set; }

        public IEnumerable<CoinViewModel> CoinList => _coinViewModels;


        public bool CanDequeueCoins { get; set; } = false;

        private void ClearRootList() => RootList.Clear();

        public void SelectPrivateCoins() => SelectCoins(x => x.AnonymitySet >= _config.Value.PrivacyLevelSome);

        public void AfterDismissed()
        {
            ClearRootList();

            Disposables?.Dispose();
        }

        public Money SelectedAmount
        {
            get => _selectedAmount;
            set => this.RaiseAndSetIfChanged(ref _selectedAmount, value);
        }

        public bool IsCoinListLoading
        {
            get => _isCoinListLoading;
            set => this.RaiseAndSetIfChanged(ref _isCoinListLoading, value);
        }

        public bool IsAnyCoinSelected
        {
            get => _isAnyCoinSelected;
            set => this.RaiseAndSetIfChanged(ref _isAnyCoinSelected, value);
        }

        public int SelectedCount
        {
            get => _selectedCount;
            set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
        }

        public void SelectOnlyPrivateCoins(bool onlyPrivate)
        {
            foreach (var c in CoinList)
            {
                c.IsSelected = !onlyPrivate || c.AnonymitySet > 1;
            }
        }

        public bool WarnCertainLink
        {
            get => _warnCertainLink;
            set => this.RaiseAndSetIfChanged(ref _warnCertainLink, value);
        }
    }
}
