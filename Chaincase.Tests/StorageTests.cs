using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Common.Services;
using NBitcoin;
using WalletWasabi.Helpers;
using Xunit;
using Cryptor = Chaincase.Common.Services.AesThenHmac;


namespace Chaincase.Tests
{
    public class StorageTests
    {
        [Fact]
        public void CanStoreSeedWords()
        {
            var testDir = EnvironmentHelpers.GetDataDir(Path.Combine("Chaincase", "Tests", "StorageTests"));
            var config = new Config(Path.Combine(testDir, "Config.json"));
            var uiConfig = new UiConfig(Path.Combine(testDir, "UiConfig.json"));

            SensitiveStorage storage = new(new MockHsm(), config, uiConfig);
            string password = "password";
            Mnemonic mnemonic = new(Wordlist.English);
            storage.SetSeedWords(password, mnemonic.ToString());
            var gotSeedWords = storage.GetSeedWords(password).Result;
            Assert.True(gotSeedWords == mnemonic.ToString());
        }

        [Fact]
        public void CanDecryptCiphertextFromPython()
        {
            // generated with python
            string password = "password";
            string b64CipherText = "Gup4moWGF4RRcyPUErUuctQE2MlgH7hHIiy0+gxNT3Mc+Ktax/t25W47Lk4jOJt0QT8W2LhkwH8qg28qZ2bM0XozLEIPZe/mi9BuryrMJX8=";
            var plaintext = Cryptor.DecryptWithPassword(b64CipherText, password);
            Assert.True(plaintext == "poops");
        }

        [Fact]
        public async void CanEncryptAndDecryptFiles()
		{
            string text =
            "A class is the most powerful data type in C#. Like a structure, " +
            "a class defines the data and behavior of the data type. ";

            await File.WriteAllTextAsync("WriteText.txt", text);

            var key = Cryptor.NewKey();
            Cryptor.EncryptFile("WriteText.txt", key);
            Cryptor.DecryptFile("WriteText.txt.aes.hmac", key);

            string decryptedText = await File.ReadAllTextAsync("WriteText.txt");
            Assert.True(text == decryptedText);
        }
    }

    class MockHsm : IHsmStorage
    {
        private Dictionary<string, string> keyStore = new();

        public Task<string> GetAsync(string key)
        {
            var tcs = new TaskCompletionSource<string>();
            if (!keyStore.TryGetValue(key, out var value))
                throw new Exception();
            tcs.SetResult(value);
            return tcs.Task;
        }

        public bool Remove(string key)
        {
            return true;
        }

        public Task SetAsync(string key, string value)
        {
            keyStore.AddOrReplace(key, value);
            return Task.CompletedTask;
        }
    }
}
