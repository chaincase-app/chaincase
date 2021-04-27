using System;
using System.IO;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Microsoft.Extensions.Options;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace Chaincase.UI.ViewModels
{
	public class LoadWalletViewModel : ReactiveObject
	{
		private readonly WalletManager _walletManager;
		private readonly IOptions<Config> _config;
		private readonly IOptions<UiConfig> _uiConfig;
		private readonly IHsmStorage _hsm;

		private string _password;
		private string _seedWords;

		private readonly string ACCOUNT_KEY_PATH = $"m/{KeyManager.DefaultAccountKeyPath}";
		private const int MIN_GAP_LIMIT = KeyManager.AbsoluteMinGapLimit * 4;

		public LoadWalletViewModel(WalletManager walletManager, IOptions<Config> config, IOptions<UiConfig> uiConfig, IHsmStorage hsmStorage)
		{
			_walletManager = walletManager;
			_config = config;
			_uiConfig = uiConfig;
			_hsm = hsmStorage;
		}

		public bool LoadWallet()
		{
			SeedWords = Guard.Correct(SeedWords);
			Password = Guard.Correct(Password); // Do not let whitespaces to the beginning and to the end.

			string walletFilePath = Path.Combine(_walletManager.WalletDirectories.WalletsDir, $"{_config.Value.Network}.json");
			bool isLoadSuccessful;

			try
			{
				KeyPath.TryParse(ACCOUNT_KEY_PATH, out KeyPath keyPath);

				var mnemonic = new Mnemonic(SeedWords);
				var km = KeyManager.Recover(mnemonic, Password, filePath: null, keyPath, MIN_GAP_LIMIT);
				km.SetNetwork(_config.Value.Network);
				km.SetFilePath(walletFilePath);
				_walletManager.AddWallet(km);
				_hsm.SetAsync($"{_config.Value.Network}-seedWords", SeedWords.ToString()); // PROMPT
				_uiConfig.Value.HasSeed = true;
				_uiConfig.Value.ToFile();
				isLoadSuccessful = true;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				isLoadSuccessful =  false;
			}
			return isLoadSuccessful;
		}

		public string Password
		{
			get => _password;
			set => this.RaiseAndSetIfChanged(ref _password, value);
		}

		public string SeedWords
		{
			get => _seedWords;
			set => this.RaiseAndSetIfChanged(ref _seedWords, value);
		}
	}
}
