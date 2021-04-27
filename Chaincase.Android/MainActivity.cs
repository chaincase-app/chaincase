using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.OS;
using Chaincase.Common.Contracts;
using Chaincase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.MobileBlazorBindings.WebView.Android;

namespace Chaincase.Droid
{
	[Activity(LaunchMode = LaunchMode.SingleTop, Label = "Chaincase", Icon = "@mipmap/icon",
		Theme = "@style/MainTheme", MainLauncher = true,
		ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
	public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
	{
		protected override void OnCreate(Bundle savedInstanceState)
		{
			BlazorHybridAndroid.Init();
			var fileProvider = new AssetFileProvider(Assets, "wwwroot");

			base.OnCreate(savedInstanceState);

			Xamarin.Essentials.Platform.Init(this, savedInstanceState);
			ZXing.Net.Mobile.Forms.Android.Platform.Init();
			global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
			LoadApplication(new BlazorApp(fileProvider, ConfigureDi));
		}

		private void ConfigureDi(IServiceCollection obj)
		{
			obj.AddSingleton<IHsmStorage, XamarinHsmStorage>();
			obj.AddSingleton<ITorManager, MockTorManager>();
			obj.AddSingleton<INotificationManager, MockNotificationManager>();
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
			[GeneratedEnum] Android.Content.PM.Permission[] grantResults)
		{
			Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
			ZXing.Net.Mobile.Android.PermissionsHandler.OnRequestPermissionsResult(requestCode, permissions,
				grantResults);
			base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
		}
	}
}
