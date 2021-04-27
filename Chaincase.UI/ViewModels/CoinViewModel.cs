using System;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Chaincase.Common;
using Chaincase.Common.Models;
using Chaincase.Common.Services;
using Microsoft.Extensions.Options;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.CoinJoin.Client.Rounds;
using WalletWasabi.CoinJoin.Common.Models;
using WalletWasabi.Models;
using WalletWasabi.Stores;

namespace Chaincase.UI.ViewModels
{
	public class CoinViewModel : ReactiveObject
	{
		private readonly ChaincaseWalletManager _walletManager;
		private readonly IOptions<Config> _config;
		private readonly BitcoinStore _bitcoinStore;

		public CompositeDisposable Disposables { get; set; }

		private bool _isSelected;
		private SmartCoinStatus _status;
		private ObservableAsPropertyHelper<bool> _coinJoinInProgress;
		private ObservableAsPropertyHelper<bool> _unspent;
		private ObservableAsPropertyHelper<bool> _confirmed;
		private ObservableAsPropertyHelper<bool> _unavailable;
		private ObservableAsPropertyHelper<string> _cluster;

		public ReactiveCommand<Unit, Unit> NavBackCommand;

		public CoinViewModel(ChaincaseWalletManager walletManager, IOptions<Config> config, BitcoinStore bitcoinStore, SmartCoin model)
		{
			_walletManager = walletManager;
			_config = config;
			_bitcoinStore = bitcoinStore;
			Model = model;

			Disposables = new CompositeDisposable();

			_coinJoinInProgress = Model.WhenAnyValue(x => x.CoinJoinInProgress)
				.ObserveOn(RxApp.MainThreadScheduler)
				.ToProperty(this, x => x.CoinJoinInProgress)
				.DisposeWith(Disposables);

			_unspent = Model.WhenAnyValue(x => x.Unspent).ToProperty(this, x => x.Unspent, scheduler: RxApp.MainThreadScheduler)
				.DisposeWith(Disposables);

			_confirmed = Model.WhenAnyValue(x => x.Confirmed).ToProperty(this, x => x.Confirmed, scheduler: RxApp.MainThreadScheduler)
				.DisposeWith(Disposables);

			_unavailable = Model.WhenAnyValue(x => x.Unavailable).ToProperty(this, x => x.Unavailable, scheduler: RxApp.MainThreadScheduler)
				.DisposeWith(Disposables);

			this.WhenAnyValue(x => x.Status)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => this.RaisePropertyChanged(nameof(ToolTip)));

			_cluster = Model
				.WhenAnyValue(x => x.Clusters, x => x.Clusters.Labels)
				.Select(x => x.Item2.ToString())
				.ToProperty(this, x => x.Clusters, scheduler: RxApp.MainThreadScheduler)
				.DisposeWith(Disposables);

			Observable
				.Merge(Model.WhenAnyValue(x => x.IsBanned, x => x.SpentAccordingToBackend, x => x.CoinJoinInProgress).Select(_ => Unit.Default))
				.Merge(Observable.FromEventPattern(_walletManager.CurrentWallet.ChaumianClient, nameof(_walletManager.CurrentWallet.ChaumianClient.StateUpdated)).Select(_ => Unit.Default))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => RefreshSmartCoinStatus())
				.DisposeWith(Disposables);

			_bitcoinStore.SmartHeaderChain
				.WhenAnyValue(x => x.TipHeight).Select(_ => Unit.Default)
				.Merge(Model.WhenAnyValue(x => x.Height).Select(_ => Unit.Default))
				.Throttle(TimeSpan.FromSeconds(0.1)) // DO NOT TAKE THIS THROTTLE OUT, OTHERWISE SYNCING WITH COINS IN THE WALLET WILL STACKOVERFLOW!
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => this.RaisePropertyChanged(nameof(Confirmations)))
				.DisposeWith(Disposables);

		}

		public SmartCoin Model { get; }

		public bool Confirmed => _confirmed?.Value ?? false;

		public bool CoinJoinInProgress => _coinJoinInProgress?.Value ?? false;

		public bool Unavailable => _unavailable?.Value ?? false;

		public bool Unspent => _unspent?.Value ?? false;

		public string Address => Model.ScriptPubKey.GetDestinationAddress(_config.Value.Network).ToString();

		public int Confirmations => Model.Height.Type == HeightType.Chain
			? (int)_bitcoinStore.SmartHeaderChain.TipHeight - Model.Height.Value + 1
			: 0;

		public string ToolTip
		{
			get
			{
				switch (Status)
				{
					case SmartCoinStatus.Confirmed: return "This coin is confirmed.";
					case SmartCoinStatus.Unconfirmed: return "This coin is unconfirmed.";
					case SmartCoinStatus.MixingOnWaitingList: return "This coin is waiting for its turn to be coinjoined.";
					case SmartCoinStatus.MixingBanned: return $"The coordinator banned this coin from participation until {Model?.BannedUntilUtc?.ToString("yyyy - MM - dd HH: mm", CultureInfo.InvariantCulture)}.";
					case SmartCoinStatus.MixingInputRegistration: return "This coin is registered for coinjoin.";
					case SmartCoinStatus.MixingConnectionConfirmation: return "This coin is currently in Connection Confirmation phase.";
					case SmartCoinStatus.MixingOutputRegistration: return "This coin is currently in Output Registration phase.";
					case SmartCoinStatus.MixingSigning: return "This coin is currently in Signing phase.";
					case SmartCoinStatus.SpentAccordingToBackend: return "According to the Backend, this coin is spent. Wallet state will be corrected after confirmation.";
					case SmartCoinStatus.MixingWaitingForConfirmation: return "Coinjoining unconfirmed coins is not allowed, unless the coin is a coinjoin output itself.";
					default: return "This is impossible.";
				}
			}
		}

		public Money Amount => Model.Amount;

		public string AmountBtc => Model.Amount.ToString(false, true);

		public string Label => Model.Label;

		public int Height => Model.Height;

		public string TransactionId => Model.TransactionId.ToString();

		public uint OutputIndex => Model.Index;

		public int AnonymitySet => Model.AnonymitySet;

		public string InCoinJoin => Model.CoinJoinInProgress ? "Yes" : "No";

		public string Clusters => _cluster?.Value ?? "";

		public string PubKey => Model.HdPubKey.PubKey.ToString();

		public string KeyPath => Model.HdPubKey.FullKeyPath.ToString();

		public SmartCoinStatus Status
		{
			get => _status;
			set => this.RaiseAndSetIfChanged(ref _status, value);
		}

		public bool IsSelected
		{
			get => _isSelected;
			set => this.RaiseAndSetIfChanged(ref _isSelected, value);
		}

		private void RefreshSmartCoinStatus()
		{
			Status = GetSmartCoinStatus();
		}

		private SmartCoinStatus GetSmartCoinStatus()
		{
			Model.SetIsBanned(); // Recheck if the coin's ban has expired.
			if (Model.IsBanned)
			{
				return SmartCoinStatus.MixingBanned;
			}

			if (Model.CoinJoinInProgress && _walletManager.CurrentWallet.ChaumianClient != null)
			{
				ClientState clientState = _walletManager.CurrentWallet.ChaumianClient.State;
				foreach (var round in clientState.GetAllMixingRounds())
				{
					if (round.CoinsRegistered.Contains(Model))
					{
						if (round.State.Phase == RoundPhase.InputRegistration)
						{
							return SmartCoinStatus.MixingInputRegistration;
						}
						else if (round.State.Phase == RoundPhase.ConnectionConfirmation)
						{
							return SmartCoinStatus.MixingConnectionConfirmation;
						}
						else if (round.State.Phase == RoundPhase.OutputRegistration)
						{
							return SmartCoinStatus.MixingOutputRegistration;
						}
						else if (round.State.Phase == RoundPhase.Signing)
						{
							return SmartCoinStatus.MixingSigning;
						}
					}
				}
			}

			if (Model.SpentAccordingToBackend)
			{
				return SmartCoinStatus.SpentAccordingToBackend;
			}

			if (Model.Confirmed)
			{
				if (Model.CoinJoinInProgress)
				{
					return SmartCoinStatus.MixingOnWaitingList;
				}
				else
				{
					return SmartCoinStatus.Confirmed;
				}
			}
			else // Unconfirmed
			{
				if (Model.CoinJoinInProgress)
				{
					return SmartCoinStatus.MixingWaitingForConfirmation;
				}
				else
				{
					return SmartCoinStatus.Unconfirmed;
				}
			}
		}

		public CompositeDisposable GetDisposables() => Disposables;

		#region IDisposable Support

		private volatile bool _disposedValue = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					Disposables?.Dispose();
				}

				Disposables = null;
				_disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

		#endregion IDisposable Support
	}
}
