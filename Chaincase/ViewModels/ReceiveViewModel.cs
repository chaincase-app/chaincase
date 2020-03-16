﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using Xamarin.Forms;

namespace Chaincase.ViewModels
{
	public class ReceiveViewModel : ViewModelBase
	{
		private string _memo;
		public string Memo
		{
			get => _memo;
			set => this.RaiseAndSetIfChanged(ref _memo, value);
		}

		public ReactiveCommand<Unit, Unit> GenerateCommand { get; }

		public ReceiveViewModel(IScreen hostScreen) : base(hostScreen)
		{
			GenerateCommand = ReactiveCommand.CreateFromObservable(() =>
			{
				Device.BeginInvokeOnMainThread(() =>
				{
                    HdPubKey toReceive = Global.WalletService.KeyManager.GetNextReceiveKey(Memo, out bool minGapLimitIncreased);
                    if (minGapLimitIncreased)
                    {
                        int minGapLimit = Global.WalletService.KeyManager.MinGapLimit.Value;
                        int prevMinGapLimit = minGapLimit - 1;
                    }
                    Memo = null;
					HostScreen.Router.Navigate.Execute(new AddressViewModel(hostScreen, toReceive)).Subscribe();
				});
				return Observable.Return(Unit.Default);
			});
		}
	}
}