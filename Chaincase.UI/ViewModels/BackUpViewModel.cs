using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Chaincase.UI.ViewModels
{
    public class BackUpViewModel : ReactiveObject
    {
        private IOptions<Config> Config { get; }
        private IOptions<UiConfig> UiConfig { get; }
        protected IHsmStorage HSM { get; }

        private List<string> _seedWords;

        public BackUpViewModel(IOptions<Config> config, IOptions<UiConfig> uiConfig, IHsmStorage hsm)
        {
            Config = config;
            UiConfig = uiConfig;
            HSM = hsm;
        }

        public async Task<bool> HasGotSeedWords()
        {
            var seedWords = await HSM.GetAsync($"{Config.Value.Network}-seedWords");
            if (seedWords is null) return false;

            SeedWords = seedWords.Split(' ').ToList();
            return true;
        }

        public void SetIsBackedUp()
        {
            UiConfig.Value.IsBackedUp = true;
            UiConfig.Value.ToFile(); // successfully backed up!
        }

        public List<string> SeedWords
        {
            get => _seedWords;
            set => this.RaiseAndSetIfChanged(ref _seedWords, value);
        }
    }
}
