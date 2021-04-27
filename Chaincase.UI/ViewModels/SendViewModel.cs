using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Chaincase.Common;
using NBitcoin;
using ReactiveUI;
using Splat;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Helpers;
using Chaincase.UI.Services;
using NBitcoin.Payment;
using WalletWasabi.Logging;
using WalletWasabi.Blockchain.Transactions;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Exceptions;
using Chaincase.Common.Services;
using Chaincase.Common.Contracts;
using Microsoft.Extensions.Options;
using WalletWasabi.Blockchain.TransactionBroadcasting;

namespace Chaincase.UI.ViewModels
{
    public class SendViewModel : ReactiveObject
    {
        private TransactionBroadcaster _transactionBroadcaster;

        private readonly Global _global;
        private readonly ChaincaseWalletManager _walletManager;
        private readonly IOptions<Config> _config;
        private readonly IOptions<UiConfig> _uiConfig;
        private readonly FeeProviders _feeProviders;

        private bool _isMax;
        private string _amountText;
        readonly ObservableAsPropertyHelper<string> _sendFromText;
        private FeeRate _feeRate;
        private Money _allSelectedAmount;
        private Money _estimatedBtcFee;
        private int _feeTarget;
        private int _minimumFeeTarget;
        private int _maximumFeeTarget;
        private ObservableAsPropertyHelper<bool> _minMaxFeeTargetsEqual;
        private readonly ObservableAsPropertyHelper<bool> _isTransactionOkToSign;

        private readonly ObservableAsPropertyHelper<BitcoinUrlBuilder> _destinationUrl;
        private readonly ObservableAsPropertyHelper<Money> _outputAmount;
        private string _destinationString;
        private bool _isBusy;
        private string _label;
        private SelectCoinsViewModel _selectCoinsViewModel;
        private SmartTransaction _signedTransaction;

        protected CompositeDisposable Disposables { get; } = new CompositeDisposable();

        public SendViewModel(Global global, ChaincaseWalletManager walletManager, IOptions<Config> config, IOptions<UiConfig> uiConfig, SelectCoinsViewModel selectCoinsViewModel, FeeProviders feeProviders)
        {
            _global = global;
            _walletManager = walletManager;
            _config = config;
            _uiConfig = uiConfig;
            _feeProviders = feeProviders;

            SelectCoinsViewModel = selectCoinsViewModel;
            AmountText = "0.0";
            AllSelectedAmount = Money.Zero;
            EstimatedBtcFee = Money.Zero;

            if (_global.IsInitialized)
            {
                OnAppInitialized(this, new AppInitializedEventArgs(_global));
            }
            else
            {
                _global.Initialized += OnAppInitialized;
            }

            _outputAmount = this.WhenAnyValue(x => x.AmountText,
                (amountText) =>
                {
                    Money.TryParse(amountText.TrimStart('~', ' '), out Money outputAmount);
                    return outputAmount;
                }).ToProperty(this, x => x.OutputAmount);

            this.WhenAnyValue(x => x.IsMax)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(isMax =>
                {
                    if (isMax)
                        SetAmountIfMax();
                });

            Observable.FromEventPattern(SelectCoinsViewModel, nameof(SelectCoinsViewModel.SelectionChanged))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ApplyFees());

            _minMaxFeeTargetsEqual = this.WhenAnyValue(x => x.MinimumFeeTarget, x => x.MaximumFeeTarget, (x, y) => x == y)
                .ToProperty(this, x => x.MinMaxFeeTargetsEqual, scheduler: RxApp.MainThreadScheduler);

            SetFeeTargetLimits();
            FeeTarget = _uiConfig.Value.FeeTarget;
            FeeRate = new FeeRate((decimal)50); //50 sat/vByte placeholder til loads

            this.WhenAnyValue(x => x.FeeTarget)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    ApplyFees();
                });

            _destinationUrl = this.WhenAnyValue(x => x.DestinationString, ParseDestinationString)
                .ToProperty(this, nameof(DestinationUrl));

            var isTransactionOkToSign = this.WhenAnyValue(
                x => x.Label, x => x.DestinationUrl, x => x.OutputAmount,
                x => x.SelectCoinsViewModel.SelectedAmount,
                x => x.EstimatedBtcFee,
            (label, address, outputAmount, selectedAmount, feeAmount) =>
            {
                return label.NotNullAndNotEmpty()
                    && DestinationUrl.Address is not null
                    && outputAmount > Money.Zero
                    && outputAmount + feeAmount <= selectedAmount;

            });

            _isTransactionOkToSign = isTransactionOkToSign
                .ToProperty(this, x => x.IsTransactionOkToSign);

            SendTransactionCommand = ReactiveCommand.CreateFromTask<string, bool>(SendTransaction, isTransactionOkToSign);
        }

        public void OnAppInitialized(object sender, AppInitializedEventArgs args)
        {
            _transactionBroadcaster = args.TransactionBroadcaster;

            Observable
               .FromEventPattern<AllFeeEstimate>(_feeProviders, nameof(_feeProviders.AllFeeEstimateChanged))
               .ObserveOn(RxApp.MainThreadScheduler)
               .Subscribe(_ =>
               {
                   SetFeeTargetLimits();

                   if (FeeTarget < MinimumFeeTarget) // Should never happen.
                   {
                       FeeTarget = MinimumFeeTarget;
                   }
                   else if (FeeTarget > MaximumFeeTarget)
                   {
                       FeeTarget = MaximumFeeTarget;
                   }
               })
               .DisposeWith(Disposables);
        }

        internal BitcoinUrlBuilder ParseDestinationString(string destinationString)
        {
            if (destinationString == null) return null;
            destinationString = destinationString.Trim();
            BitcoinUrlBuilder url = null;
            try
            {
                url = new BitcoinUrlBuilder(destinationString, _config.Value.Network);
                if (url.Amount != null)
                {
                    // since AmountText can be altered by hand, we set it instead
                    // of binding to a calculated ObservableAsPropertyHelper
                    AmountText = url.Amount.ToString();
                }
                // we could check url.Label or url.Message for contact, but there is
                // no convention on their use yet so it's hard to say whether they
                // identify the sender or receiver. We care about the recipient only here.
                return url;
            }
            catch (Exception) { /* invalid bitcoin uri */ }

            try
            {
                BitcoinAddress address = BitcoinAddress.Create(destinationString.Trim(), _config.Value.Network);
                url = new BitcoinUrlBuilder();
                url.Address = address;
            }
            catch (Exception) { /* invalid bitcoin address */ }

            return url;
        }

        public void SetAmountText()
        {
            if (IsMax)
            {
                SetAmountIfMax();
            }
            else
            {
                Regex digitsOnly = new Regex(@"[^\d,.]");
                string betterAmount = digitsOnly.Replace(AmountText, ""); // Make it digits , and . only.

                betterAmount = betterAmount.Replace(',', '.');
                int countBetterAmount = betterAmount.Count(x => x == '.');
                if (countBetterAmount > 1) // Do not enable typing two dots.
                {
                    var index = betterAmount.IndexOf('.', betterAmount.IndexOf('.') + 1);
                    if (index > 0)
                    {
                        betterAmount = betterAmount.Substring(0, index);
                    }
                }
                var dotIndex = betterAmount.IndexOf('.');
                if (dotIndex != -1 && betterAmount.Length - dotIndex > 8) // Enable max 8 decimals.
                {
                    betterAmount = betterAmount.Substring(0, dotIndex + 1 + 8);
                }

                if (betterAmount != AmountText)
                {
                    AmountText = betterAmount;
                }
            }
        }

        private void ApplyFees()
        {
            AllFeeEstimate allFeeEstimate = _feeProviders?.AllFeeEstimate;

            if (allFeeEstimate is { })
            {
                int feeTarget = FeeTarget;

                int prevKey = allFeeEstimate.Estimations.Keys.First();
                foreach (int target in allFeeEstimate.Estimations.Keys)
                {
                    if (feeTarget == target)
                    {
                        break;
                    }
                    else if (feeTarget < target)
                    {
                        feeTarget = prevKey;
                        break;
                    }
                    prevKey = target;
                }

                FeeRate = allFeeEstimate.GetFeeRate(feeTarget);

                IEnumerable<SmartCoin> selectedCoins = SelectCoinsViewModel.CoinList.Where(cvm => cvm.IsSelected).Select(x => x.Model);

                int vsize = 150;
                if (selectedCoins.Any())
                {
                    if (Money.TryParse(AmountText.TrimStart('~', ' '), out Money amount))
                    {
                        var inNum = 0;
                        var amountSoFar = Money.Zero;
                        foreach (SmartCoin coin in selectedCoins.OrderByDescending(x => x.Amount))
                        {
                            amountSoFar += coin.Amount;
                            inNum++;
                            if (amountSoFar > amount)
                            {
                                break;
                            }
                        }
                        vsize = NBitcoinHelpers.CalculateVsizeAssumeSegwit(inNum, 2);
                    }
                }

                if (FeeRate != null)
                {
                    EstimatedBtcFee = FeeRate.GetTotalFee(vsize);
                }
                else
                {
                    // This should not happen. Never.
                    // If FeeRate is null we will have problems when building the tx.
                    EstimatedBtcFee = Money.Zero;
                }
                long all = selectedCoins.Sum(x => x.Amount);
                AllSelectedAmount = Math.Max(Money.Zero, all - EstimatedBtcFee);
                SetAmountIfMax();
            }
        }

        private void SetFeeTargetLimits()
        {
            var allFeeEstimate = _feeProviders?.AllFeeEstimate;

            if (allFeeEstimate != null)
            {
                MinimumFeeTarget = allFeeEstimate.Estimations.Min(x => x.Key); // This should be always 2, but bugs will be seen at least if it is not.
                MaximumFeeTarget = allFeeEstimate.Estimations.Max(x => x.Key);
            }
            else
            {
                MinimumFeeTarget = 2;
                MaximumFeeTarget = Constants.SevenDaysConfirmationTarget;
            }
        }

        private void SetAmountIfMax()
        {
            if (IsMax)
            {
                AmountText = AllSelectedAmount == Money.Zero
                    ? EstimatedBtcFee >= AllSelectedAmount
                        ? "Too high fee"
                        : "No Coins Selected"
                    : $"~ {AllSelectedAmount.ToString(false, true)}";
            }
        }

        public ReactiveCommand<string, bool> SendTransactionCommand;

        public async Task<bool> SendTransaction(string password)
        {
            try
            {
                IsBusy = true;
                password = Guard.Correct(password);
                Label = Label.Trim(',', ' ').Trim();

                var selectedCoinViewModels = SelectCoinsViewModel.CoinList.Where(cvm => cvm.IsSelected);
                var selectedCoinReferences = selectedCoinViewModels.Select(cvm => cvm.Model.OutPoint).ToList();

                if (!selectedCoinReferences.Any())
                {
                    //SetWarningMessage("No coins are selected to spend.");
                    return false;
                }

                var amount = Money.Zero;

                var requests = new List<DestinationRequest>();

                MoneyRequest moneyRequest;
                if (IsMax)
                {
                    moneyRequest = MoneyRequest.CreateAllRemaining(subtractFee: true);
                }
                else
                {
                    if (!Money.TryParse(AmountText, out amount) || amount == Money.Zero)
                    {
                        // SetWarningMessage($"Invalid amount.");
                        return false;
                    }

                    if (amount == selectedCoinViewModels.Sum(x => x.Amount))
                    {
                        // NotificationHelpers.Warning("Looks like you want to spend whole coins. Try Max button instead.", "");
                        return false;
                    }
                    moneyRequest = MoneyRequest.Create(amount, subtractFee: false);
                }

                if (FeeRate is null || FeeRate.SatoshiPerByte < 1)
                {
                    return false;
                }

                var feeStrategy = FeeStrategy.CreateFromFeeRate(FeeRate);

                var smartLabel = new SmartLabel(Label);
                var activeDestinationRequest = new DestinationRequest(DestinationUrl.Address, moneyRequest, smartLabel);
                requests.Add(activeDestinationRequest);
                var intent = new PaymentIntent(requests);

                var result = await Task.Run(() => _walletManager.CurrentWallet.BuildTransaction(
                    password,
                    intent,
                    feeStrategy,
                    allowUnconfirmed: true,
                    allowedInputs: selectedCoinReferences));
                SmartTransaction signedTransaction = result.Transaction;
                SignedTransaction = signedTransaction;

                await _transactionBroadcaster.SendTransactionAsync(signedTransaction); // put this on non-ui theread?

                return true;
            }
            catch (InsufficientBalanceException ex)
            {
                Money needed = ex.Minimum - ex.Actual;
                Logger.LogDebug(ex);
                //SetWarningMessage($"Not enough coins selected. You need an estimated {needed.ToString(false, true)} BTC more to make this transaction.");
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex);
                //SetWarningMessage(ex.ToTypeMessageString());
            }
            finally
            {
                IsBusy = false;
            }
            return false;
        }

        public BitcoinUrlBuilder DestinationUrl => _destinationUrl.Value;

        public Money OutputAmount => _outputAmount.Value;

        public bool IsTransactionOkToSign => _isTransactionOkToSign.Value;

        public bool IsMax
        {
            get => _isMax;
            set => this.RaiseAndSetIfChanged(ref _isMax, value);
        }

        public string AmountText
        {
            get => _amountText;
            set => this.RaiseAndSetIfChanged(ref _amountText, value);
        }
        public FeeRate FeeRate
        {
            get => _feeRate;
            set => this.RaiseAndSetIfChanged(ref _feeRate, value);
        }

        public Money AllSelectedAmount
        {
            get => _allSelectedAmount;
            set => this.RaiseAndSetIfChanged(ref _allSelectedAmount, value);
        }

        public Money EstimatedBtcFee
        {
            get => _estimatedBtcFee;
            set => this.RaiseAndSetIfChanged(ref _estimatedBtcFee, value);
        }

        public int FeeTarget
        {
            get => _feeTarget;
            set
            {
                this.RaiseAndSetIfChanged(ref _feeTarget, value);
            }
        }

        public int MinimumFeeTarget
        {
            get => _minimumFeeTarget;
            set => this.RaiseAndSetIfChanged(ref _minimumFeeTarget, value);
        }

        public int MaximumFeeTarget
        {
            get => _maximumFeeTarget;
            set => this.RaiseAndSetIfChanged(ref _maximumFeeTarget, value);
        }

        public bool MinMaxFeeTargetsEqual => _minMaxFeeTargetsEqual.Value;

        public SelectCoinsViewModel SelectCoinsViewModel
        {
            get => _selectCoinsViewModel;
            set => this.RaiseAndSetIfChanged(ref _selectCoinsViewModel, value);
        }

        public string SendFromText => _sendFromText.Value;

        public string DestinationString
        {
            get => _destinationString;
            set => this.RaiseAndSetIfChanged(ref _destinationString, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }

        public SmartTransaction SignedTransaction
        {
            get => _signedTransaction;
            set => this.RaiseAndSetIfChanged(ref _signedTransaction, value);
        }
    }
}
