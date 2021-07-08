using Chaincase.Background;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.iOS.Services;
using Chaincase.iOS.Background;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using UIKit;
using UserNotifications;
using Xamarin.Forms;
using Splat;

namespace Chaincase.iOS
{
	// The UIApplicationDelegate for the application. This class is responsible for launching the
	// User Interface of the application, as well as listening (and optionally responding) to
	// application events from iOS.
	[Register("AppDelegate")]
    public partial class AppDelegate : global::Xamarin.Forms.Platform.iOS.FormsApplicationDelegate
    {
        //
        // This method is invoked when the application has loaded and is ready to run. In this
        // method you should instantiate the window, load the UI into it and then make the window
        // visible.
        //
        // You have 17 seconds to return from this method, or iOS will terminate your application.
        //
        public override bool FinishedLaunching(UIApplication app, NSDictionary options)
        {
            global::Xamarin.Forms.Forms.Init();

            ZXing.Net.Mobile.Forms.iOS.Platform.Init();
            var formsApp = new BlazorApp(fileProvider: null, ConfigureDi);
            var serviceProvider = formsApp.ServiceProvider;

            MessagingCenter.Subscribe<InitializeNoWalletTaskMessage>(this, "InitializeNoWalletTaskMessage", async message =>
            {
                var context = new iOSInitializeNoWalletContext(serviceProvider.GetService<Global>());
                await context.InitializeNoWallet();
            });

            MessagingCenter.Subscribe<OnSleepingTaskMessage>(this, "OnSleepingTaskMessage", async message =>
            {
                var context = new iOSOnSleepingContext(serviceProvider.GetService<Global>());
                await context.OnSleeping();
            });

            formsApp.InitializeNoWallet();

            UNUserNotificationCenter.Current.Delegate = 
                formsApp.ServiceProvider.GetService<iOSNotificationReceiver>();
            LoadApplication(formsApp);
            UIApplication.SharedApplication.IdleTimerDisabled = true;
            return base.FinishedLaunching(app, options);
        }

        private void ConfigureDi(IServiceCollection obj)
        {
	        obj.AddSingleton<IHsmStorage, iOSHsmStorage>();
	        obj.AddSingleton<INotificationManager, iOSNotificationManager>();
	        obj.AddSingleton<ITorManager, iOSTorManager>();
	        obj.AddSingleton<iOSNotificationReceiver>();
        }
    }
}
