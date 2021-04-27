using System.IO;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Services;
using Microsoft.Extensions.Options;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;

namespace Chaincase.UI.ViewModels
{
    public class PINViewModel : ReactiveObject
    {
        private readonly ChaincaseWalletManager _walletManager;
        private readonly IOptions<Config> _config;
        public bool IsBusy { get; set; }

        public PINViewModel(ChaincaseWalletManager walletManager, IOptions<Config> config)
        {
            _walletManager = walletManager;
            _config = config;
        }

        public async Task IsPasswordValidAsync(string password)
        {
            IsBusy = true;
            string walletFilePath = Path.Combine(_walletManager.WalletDirectories.WalletsDir, $"{_config.Value.Network}.json");
            try
            {
                await Task.Run(() => KeyManager.FromFile(walletFilePath).GetMasterExtKey(password ?? ""));
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
