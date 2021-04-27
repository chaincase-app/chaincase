using System;
using System.IO;
using System.Threading.Tasks;
using Chaincase.Background;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Services;
using Chaincase.UI.Services;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.MobileBlazorBindings;
using WalletWasabi.Logging;
using Xamarin.Forms;

namespace Chaincase
{
	//wip logic in case we needed to prefix json config after it was loaded
	// public class ChaincaseJsonConfigurationSource: JsonConfigurationSource
	// {
	// 	public string KeyPrefix { get; set; }
	// 	public override IConfigurationProvider Build(IConfigurationBuilder builder)
	// 	{
	// 		return new ChaincaseJsonConfigurationProvider(base.Build(builder));
	// 	}
	//
	// 	public class ChaincaseJsonConfigurationProvider : JsonConfigurationProvider
	// 	{
	// 		public ChaincaseJsonConfigurationProvider(JsonConfigurationSource source) : base(source)
	// 		{
	// 		}
	//
	// 		public override void Load(Stream stream)
	// 		{
	// 			base.Load(stream);
	// 			if (!string.IsNullOrEmpty(KeyPrefix))
	// 			{
	// 				foreach (var keyValuePair in Data)
	// 				{
	// 					keyValuePair.Key = "";
	// 				}
	// 			}
	// 		}
	// 	}
	// }
	//
	public class BlazorApp : Application
    {
        private readonly IHost _host;

        public IServiceProvider ServiceProvider => _host.Services;

        private event EventHandler Resuming = delegate { };

        public BlazorApp(IFileProvider fileProvider = null, Action<IServiceCollection> configureDI = null)
        {
	        var dataDirProvider = new XamarinDataDirProvider();
            var hostBuilder = MobileBlazorBindingsHost.CreateDefaultBuilder()
	            .ConfigureAppConfiguration(builder => builder.Add(new JsonConfigurationSource()
	            {
		            Path = Path.Combine(dataDirProvider.Get(), Config.FILENAME),
		            Optional = true
	            }))
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddCommonServices();
                    services.AddUIServices();

                    // Adds web-specific services such as NavigationManager
                    services.AddBlazorHybrid();

                    services.AddScoped<IClipboard, XamarinClipboard>();
                    services.AddSingleton<IDataDirProvider>(dataDirProvider);
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
