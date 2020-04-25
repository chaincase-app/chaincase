﻿using System.Reactive.Disposables;
using ReactiveUI;
using ReactiveUI.XamForms;
using Chaincase.ViewModels;
using Xamarin.Forms;
using System.Reactive.Linq;
using System.Reactive;
using System;

namespace Chaincase.Views
{
	public partial class MainPage : ReactiveContentPage<MainViewModel>
	{

        public MainPage()
        {
            InitializeComponent();

            this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel,
                    vm => vm.Balance,
                    v => v.Balance.Text,
                    AddBalanceSymbol)
                    .DisposeWith(d);
                this.OneWayBind(ViewModel,
                    vm => vm.StatusViewModel.Status, 
                    v => v.Balance.TextColor,
                    ConvertStatusToColor)
                    .DisposeWith(d);
                this.OneWayBind(ViewModel,
                    vm => vm.Transactions,
                    v => v.Transactions.ItemsSource)
                    .DisposeWith(d);
                this.Bind(ViewModel,
                    vm => vm.StatusViewModel,
                    v => v.Status.ViewModel);
                this.BindCommand(ViewModel,
                    vm => vm.NavReceiveCommand,
                    v => v.NavReceiveCommand)
                    .DisposeWith(d);
                this.BindCommand(ViewModel,
                    vm => vm.InitCoinJoin,
                    v => v.CoinJoinButton)
                    .DisposeWith(d);
                this.OneWayBind(ViewModel,
                    vm => vm.HasCoins,
                    v => v.CoinJoinButton.IsVisible)
                    .DisposeWith(d);

                this.BindCommand(ViewModel,
                    vm => vm.PrivateSendCommand,
                    v => v.PrivateSendButton)
                    .DisposeWith(d);
                this.OneWayBind(ViewModel,
                    vm => vm.HasPrivateCoins,
                    v => v.PrivateSendButton.IsVisible)
                    .DisposeWith(d);

                this.BindCommand(ViewModel,
                    vm => vm.ExposedSendCommand,
                    v => v.ExposedSendButton)
                    .DisposeWith(d);
                this.OneWayBind(ViewModel,
                    vm => vm.HasCoins,
                    v => v.ExposedSendButton.IsVisible)
                    .DisposeWith(d);
            });
        }

        private string AddBalanceSymbol(string bal)
        {
            return "₿ " + bal;
        }

        private Color ConvertStatusToColor(string status)
        {
            if (status == "Ready")
            {
                return Color.Default;
            }
            return Color.LightGray;
        }
    }
}
