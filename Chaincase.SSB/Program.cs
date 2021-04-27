using System.IO;
using Chaincase.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;

namespace Chaincase.SSB
{
	public class Program
	{
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args)
		{
			var dataDirProvider = new SSBDataDirProvider();
			return Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.ConfigureAppConfiguration(builder => builder.Add(new JsonConfigurationSource()
					{
						Path = Path.Combine(dataDirProvider.Get(), Config.FILENAME),
						Optional = true
					})).UseStartup<Startup>();
				});
		}
	}
}
