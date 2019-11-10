using System.IO;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.KeyManagement;
using Chaincase.Controllers;
using Xunit;

namespace Chaincase.Tests
{
	public class UnitTests
	{
		[Fact]
		public void AssertGeneratedWalletCredentialsVerifyWithPassphrase()
		{
			string passphrase = "passphrase";
			Mnemonic mnemonic =  WalletController.GenerateMnemonic(passphrase, Network.Main);

			var isVerified =  WalletController.VerifyWalletCredentials(mnemonic.ToString(),  passphrase, Network.Main);
			Assert.True(isVerified);
		}

		[Fact]
		public void AssertGeneratedWalletCredentialsVerifyWithoutPassphrase()
		{
			string passphrase = ""; // cannot be null
			Mnemonic mnemonic =  WalletController.GenerateMnemonic(passphrase, Network.Main);

			var isVerified =  WalletController.VerifyWalletCredentials(mnemonic.ToString(), passphrase, Network.Main);
			Assert.True(isVerified);
		}

		[Fact]
		public void CanLoadKeyManager()
		{
			string walletFilePath = Path.Combine(Global.WalletsDir, $"Main.json");
			KeyManager keyManager = Global.LoadKeyManager(walletFilePath);

			Assert.True(keyManager is KeyManager);
		}		
	}
}
