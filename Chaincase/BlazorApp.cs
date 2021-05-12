using System;
using System.IO;
using System.Threading.Tasks;
using Chaincase.Background;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Services;
using Chaincase.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.MobileBlazorBindings;
using WalletWasabi.Logging;
using Xamarin.Forms;

namespace Chaincase
{
	public class BlazorApp : Application
    {
        private readonly IHost _host;

        public IServiceProvider ServiceProvider => _host.Services;

        private event EventHandler Resuming = delegate { };

        public BlazorApp(IFileProvider fileProvider = null, Action<IServiceCollection> configureDI = null)
        {
            var hostBuilder = MobileBlazorBindingsHost.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddCommonServices();
                    services.AddUIServices();

                    // Adds web-specific services such as NavigationManager
                    services.AddBlazorHybrid();

                    services.AddScoped<IClipboard, XamarinClipboard>();
                    services.AddSingleton<IDataDirProvider, XamarinDataDirProvider>();
                    services.AddSingleton<IMainThreadInvoker, XamarinMainThreadInvoker>();
                    services.AddSingleton<IShare, XamarinShare>();
                    services.AddSingleton<IThemeManager, XamarinThemeManager>();

                    configureDI?.Invoke(services);
                })
                .UseWebRoot("wwwroot");

            if (fileProvider != null)
            {
                hostBuilder.UseStaticFiles(fileProvider);
            }
            else
            {
                hostBuilder.UseStaticFiles();
            }

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            _host = hostBuilder.Build();

            MainPage = new ContentPage { Title = "Chaincase" };
            _host.AddComponent<Main>(parent: MainPage);

            var dataDir = ServiceProvider.GetRequiredService<IDataDirProvider>().Get();
            Directory.CreateDirectory(dataDir);
            var config = ServiceProvider.GetRequiredService<Config>();
            config.LoadOrCreateDefaultFile();
            var uiConfig = ServiceProvider.GetRequiredService<UiConfig>();
            uiConfig.LoadOrCreateDefaultFile();
        }

        public void InitializeNoWallet()
        {
            var message = new InitializeNoWalletTaskMessage();
            MessagingCenter.Send(message, "InitializeNoWalletTaskMessage");
        }

        protected override void OnSleep()
        {
            // Execute Sleeping Background Task
            var message = new OnSleepingTaskMessage();
            MessagingCenter.Send(message, "OnSleepingTaskMessage");
        }

        protected override void OnResume()
        {
            Resuming += OnResuming;
            // Execute Async code
            Resuming(this, EventArgs.Empty);
        }

        private async void OnResuming(object sender, EventArgs args)
        {
            //unsubscribe from event
            Resuming -= OnResuming;

            //perform non-blocking actions
            await ServiceProvider.GetRequiredService<Global>().OnResuming();
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogWarning(e?.Exception, "UnobservedTaskException");
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.LogWarning(e?.ExceptionObject as Exception, "UnhandledException");
        }
    }
}
