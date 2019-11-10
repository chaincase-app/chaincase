using System.IO;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.KeyManagement;
using Chaincase.Controllers;
using Xunit;

namespace Chaincase.Tests
{
	public class TORControllerTests
	{
	[Fact]
		public void AssertGeneratedWalletCredentialsVerifyWithPassphrase()
		{
			string passphrase = "passphrase";
			Mnemonic mnemonic =  WalletController.GenerateMnemonic(passphrase, Network.Main);

			var isVerified =  WalletController.VerifyWalletCredentials(mnemonic.ToString(),  passphrase, Network.Main);
			Assert.True(isVerified);
		}
	}
}
