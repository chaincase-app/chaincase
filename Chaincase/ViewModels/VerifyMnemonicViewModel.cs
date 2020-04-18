﻿using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Chaincase.Navigation;
using Chaincase.Controllers;
using Xamarin.Forms;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;
using Splat;

namespace Chaincase.ViewModels
{
	public class VerifyMnemonicViewModel : ViewModelBase
	{
		private string _mnemonicString { get; }
		private string[] _mnemonicWords { get; }

		private string _recall0;
		public string Recall0
		{
			get => _recall0;
			set => this.RaiseAndSetIfChanged(ref _recall0, value);
		}

		private string _recall1;
		public string Recall1
		{
			get => _recall1;
			set => this.RaiseAndSetIfChanged(ref _recall1, value);
		}
		private string _recall2;
		public string Recall2
		{
			get => _recall2;
			set => this.RaiseAndSetIfChanged(ref _recall2, value);
		}
		private string _recall3;
		public string Recall3
		{
			get => _recall3;
			set => this.RaiseAndSetIfChanged(ref _recall3, value);
		}

		private bool _triedVerifyWithoutChange;
		public bool TriedVerifyWithoutChange
		{
			get => _triedVerifyWithoutChange;
			set => this.RaiseAndSetIfChanged(ref _triedVerifyWithoutChange, value);
		}

		private string _passphrase;
		public string Passphrase
		{
			get => _passphrase;
			set => this.RaiseAndSetIfChanged(ref _passphrase, value);
		}

		public VerifyMnemonicViewModel(string mnemonicString)
            : base(Locator.Current.GetService<IViewStackService>())
		{
			_mnemonicString = mnemonicString;
			_mnemonicWords = mnemonicString.Split(" ");
			Recall0 = Recall1 = Recall2 = Recall3 = "";

			VerifyCommand = ReactiveCommand.CreateFromObservable(Verify);
			VerifyCommand.Subscribe(verified =>
			{
				if (verified)
                {
					WalletController.LoadWalletAsync(Global.Network);
				    ViewStackService.PushPage(new MainViewModel()).Subscribe();
                }
			});
		}
        public IObservable<bool> Verify()
        {
			return Observable.Start(() =>
			{
				return string.Equals(Recall0, _mnemonicWords[0], StringComparison.CurrentCultureIgnoreCase) &&
					 string.Equals(Recall1, _mnemonicWords[3], StringComparison.CurrentCultureIgnoreCase) &&
					 string.Equals(Recall2, _mnemonicWords[6], StringComparison.CurrentCultureIgnoreCase) &&
					 string.Equals(Recall3, _mnemonicWords[9], StringComparison.CurrentCultureIgnoreCase) &&
					 WalletController.VerifyWalletCredentials(_mnemonicString, _passphrase, Global.Network);
			});
		}
        public ReactiveCommand<Unit, bool> VerifyCommand;
	}
}