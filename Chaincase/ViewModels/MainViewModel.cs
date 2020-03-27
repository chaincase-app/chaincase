﻿using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using Chaincase.Controllers;
using Xamarin.Forms;
using Chaincase.Navigation;
using Splat;

namespace Chaincase.ViewModels
{
	public class MainViewModel : ViewModelBase
	{
		private CompositeDisposable Disposables { get; set; }
        private CoinListViewModel _coinList;
        public CoinListViewModel CoinList
        {
            get => _coinList;
            set => this.RaiseAndSetIfChanged(ref _coinList, value);
        }

        private string _balance;
		public string Balance
		{
			get => _balance;
			set => this.RaiseAndSetIfChanged(ref _balance, value);
		}

        private String _privateBalance;
        public String PrivateBalance
        {
            get => _privateBalance;
            set => this.RaiseAndSetIfChanged(ref _privateBalance, value);
        }

        public ReactiveCommand<Unit, Unit> NavReceiveCommand;
		public ReactiveCommand<Unit, Unit> ExposedSendCommand;
        public ReactiveCommand<Unit, Unit> PrivateSendCommand;


        public ReactiveCommand<Unit, Unit> InitCoinJoin { get; private set; }
        readonly ObservableAsPropertyHelper<bool> _isJoining;
        public bool IsJoining { get { return _isJoining.Value; } }

        public Label Deq;

        private bool _hasCoins;
        public bool HasCoins
        {
            get => _hasCoins;
            set => this.RaiseAndSetIfChanged(ref _hasCoins, value);
        }

        private bool _hasPrivateCoins;
        public bool HasPrivateCoins
        {
            get => _hasPrivateCoins;
            set => this.RaiseAndSetIfChanged(ref _hasPrivateCoins, value);
        }

        public MainViewModel()
            : base(Locator.Current.GetService<IViewStackService>())
        {
            SetBalances();

            if (Disposables != null)
            {
                throw new Exception("Wallet opened before it was closed.");
            }

            Disposables = new CompositeDisposable();
            CoinList = new CoinListViewModel();

            NavReceiveCommand = ReactiveCommand.CreateFromObservable(() =>
            {
                ViewStackService.PushPage(new ReceiveViewModel()).Subscribe();
                return Observable.Return(Unit.Default);
            });

            ExposedSendCommand = ReactiveCommand.CreateFromObservable(() =>
            {
                ViewStackService.PushPage(new SendAmountViewModel(CoinList)).Subscribe();
                return Observable.Return(Unit.Default);
            });

            PrivateSendCommand = ReactiveCommand.CreateFromObservable(() =>
            {
                ViewStackService.PushPage(new SendAmountViewModel(CoinList)).Subscribe();
                return Observable.Return(Unit.Default);
            });

            InitCoinJoin = ReactiveCommand.CreateFromObservable(() =>
            {
                ViewStackService.PushPage(new CoinJoinViewModel(CoinList)).Subscribe();
                return Observable.Return(Unit.Default);
            });

            Observable.FromEventPattern(Global.WalletService.TransactionProcessor, nameof(Global.WalletService.TransactionProcessor.WalletRelevantTransactionProcessed))
				.Merge(Observable.FromEventPattern(Global.ChaumianClient, nameof(Global.ChaumianClient.OnDequeue)))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(o => {
                    SetBalances();
                })
				.DisposeWith(Disposables);
		}

		private void SetBalances()
		{
            var bal = WalletController.GetBalance();
			Balance = bal.ToString();
            HasCoins = bal > 0;

            var pbal = WalletController.GetPrivateBalance();
            PrivateBalance = pbal.ToString();
            HasPrivateCoins = pbal > 0;
        }
    }
}