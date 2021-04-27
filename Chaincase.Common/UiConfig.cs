using System.ComponentModel;
using System.IO;
using Chaincase.Common.Contracts;
using Chaincase.Common.Models;
using Newtonsoft.Json;
using WalletWasabi.Bases;

namespace Chaincase.Common
{
    [JsonObject(MemberSerialization.OptIn)]
    public class UiConfig : ConfigBase
    {
        private string _balance;
        private TransactionInfo[] _transactions = new TransactionInfo[0];
        private bool _hasSeed;
        private bool _isBackedUp;

        public UiConfig()
        {
	        
        }
        
        [DefaultValue("0")]
        [JsonProperty(PropertyName = "Balance", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string Balance
        {
            get => _balance;
            set => RaiseAndSetIfChanged(ref _balance, value);
        }

        [JsonProperty(PropertyName = "Transactions")]
        public TransactionInfo[] Transactions
        {
            get => _transactions;
            set => RaiseAndSetIfChanged(ref _transactions, value);
        }

        [DefaultValue(2)]
        [JsonProperty(PropertyName = "FeeTarget", DefaultValueHandling = DefaultValueHandling.Populate)]
        public int FeeTarget { get; internal set; }

        [JsonProperty(PropertyName = "HasSeed")]
        public bool HasSeed
        {
            get => _hasSeed;
            set => RaiseAndSetIfChanged(ref _hasSeed, value);
        }

        [JsonProperty(PropertyName = "IsBackedUp")]
        public bool IsBackedUp
        {
            get => _isBackedUp;
            set => RaiseAndSetIfChanged(ref _isBackedUp, value);
        }

        const string FILENAME = "Config.json";

        public UiConfig(IDataDirProvider dataDirProvider)
            : base(Path.Combine(dataDirProvider.Get(), FILENAME))
        {
        }

        public UiConfig(string filePath) : base(filePath)
        {
        }
    }
}
