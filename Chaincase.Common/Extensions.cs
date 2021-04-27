using System;
using System.Collections.Generic;
using System.IO;
using Chaincase.Common.Contracts;
using Chaincase.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;

namespace Chaincase.Common
{
	public static class Extensions
    {
        public static IServiceCollection AddCommonServices(this IServiceCollection services)
        {
	        services.AddOptions()
		        .ConfigureOptions<Config>()
		        .PostConfigure(new Action<Config>(config =>
		        {
			        config.MixUntilAnonymitySet = Config.GetNormalizeAnonSet(config);
		        }))
		        .ConfigureOptions<UiConfig>();
            services.AddSingleton<Global>();
            
            // services.AddSingleton<UiConfig>();
            services.AddSingleton(x => {
                var config = x.GetRequiredService<IOptions<Config>>();
                var network = config.Value.Network;
                var dataDir = x.GetRequiredService<IDataDirProvider>().Get();
                var notificationManager = x.GetRequiredService<INotificationManager>();
                return new ChaincaseWalletManager(network, new WalletDirectories(dataDir), notificationManager);
            });
            services.AddSingleton(x =>
            {
                var network = x.GetRequiredService<IOptions<Config>>().Value.Network;
                var dataDir = x.GetRequiredService<IDataDirProvider>().Get();
                var indexStore = new IndexStore(network, new SmartHeaderChain());

                return new BitcoinStore(Path.Combine(dataDir, "BitcoinStore"), network,
                    indexStore, new AllTransactionStore(), new MempoolService()
                );
            });
            services.AddSingleton(x =>
            {
                var config = x.GetRequiredService<IOptions<Config>>();
                var network = config.Value.Network;
                var bitcoinStore = x.GetRequiredService<BitcoinStore>();

                if (config.Value.UseTor)
                    return new ChaincaseSynchronizer(network, bitcoinStore, () => config.Value.GetCurrentBackendUri(), config.Value.TorSocks5EndPoint);

                return new ChaincaseSynchronizer(network, bitcoinStore, config.Value.GetFallbackBackendUri(), null);
            });
            services.AddSingleton(x =>
            {
                return new FeeProviders(new List<IFeeProvider>
                {
                    x.GetRequiredService<ChaincaseSynchronizer>()
                });
            });

            return services;
        }
    }
}
