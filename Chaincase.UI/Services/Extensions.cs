using System.Threading.Tasks;
using BlazorTransitionableRoute;
using Chaincase.Common;
using Chaincase.UI.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using QRCoder;

namespace Chaincase.UI.Services
{
	public static class Extensions
	{
		public static IServiceCollection AddUIServices(this IServiceCollection services)
		{
			services.AddSingleton<UIStateService>();

			services.AddSingleton<Global>();
			services.AddScoped<IRouteTransitionInvoker, DefaultRouteTransitionInvoker>();
			services.AddSingleton<StackService>();
			services.AddScoped<ThemeSwitcher>();
			services.AddSingleton<QRCodeGenerator>();
			services.AddSingleton<IndexViewModel>();
			services.AddSingleton<ReceiveViewModel>();
			services.AddSingleton<SendViewModel>();
			services.AddTransient<LoadWalletViewModel>();
			services.AddTransient<WalletInfoViewModel>();
			services.AddTransient<NewPasswordViewModel>();
			services.AddSingleton<SelectCoinsViewModel>();

			return services;
		}

		public static async Task NavigateBack(this NavigationManager navigationManager, IJSRuntime runtime, string route)
		{
			await runtime.InvokeVoidAsync("dispatchEventWrapper", "back");
			navigationManager.NavigateTo(route);
		}
	}
}
