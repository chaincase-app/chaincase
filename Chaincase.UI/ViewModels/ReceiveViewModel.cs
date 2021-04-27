using Chaincase.Common;
using Chaincase.Common.Services;
using Microsoft.Extensions.Options;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;

namespace Chaincase.UI.ViewModels
{
    public class ReceiveViewModel : ReactiveObject
    {
        private readonly ChaincaseWalletManager _walletManager;
        private readonly IOptions<Config> _config;

        public Money ProposedAmount { get; private set; }
        private string _proposedLabel;
        private bool[,] _qrCode;
        private string _requestAmount;

        public ReceiveViewModel(ChaincaseWalletManager walletManager, IOptions<Config> config)
        {
            _walletManager = walletManager;
            _config = config;
        }

        public void InitNextReceiveKey()
        {
            ReceivePubKey = _walletManager.CurrentWallet.KeyManager.GetNextReceiveKey(ProposedLabel, out bool minGapLimitIncreased);
            ProposedLabel = "";
        }

        public string AppliedLabel => ReceivePubKey.Label ?? "";
        public string Address => ReceivePubKey.GetP2wpkhAddress(_config.Value.Network).ToString();
        public string Pubkey => ReceivePubKey.PubKey.ToString();
        public string KeyPath => ReceivePubKey.FullKeyPath.ToString();

        public HdPubKey ReceivePubKey { get; set; }

        public string BitcoinUri => $"bitcoin:{Address}";

        public string ProposedLabel
        {
            get => _proposedLabel;
            set => this.RaiseAndSetIfChanged(ref _proposedLabel, value);
        }

        public bool[,] QrCode
        {
            get => _qrCode;
            set => this.RaiseAndSetIfChanged(ref _qrCode, value);
        }

        public string RequestAmount
        {
            get => _requestAmount;
            set => this.RaiseAndSetIfChanged(ref _requestAmount, value);
        }
    }
}
