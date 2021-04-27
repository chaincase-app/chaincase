using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Chaincase.Common.Contracts;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WalletWasabi.Bases;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.JsonConverters;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.TorSocks5;

namespace Chaincase.Common
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Config : ConfigBase
    {
        public const int DefaultPrivacyLevelSome = 2;
        public const int DefaultPrivacyLevelFine = 21;
        public const int DefaultPrivacyLevelStrong = 50;
        public const int DefaultMixUntilAnonymitySet = 50;
        public const int DefaultTorSock5Port = 9050;
        public static readonly Money DefaultDustThreshold = Money.Coins(Constants.DefaultDustThreshold);

        private int _mixUntilAnonymitySet;
        private int _privacyLevelSome;
        private int _privacyLevelFine;
        private int _privacyLevelStrong;

        public Config()
        {
	        
        }
        
        [JsonProperty(PropertyName = "Network")]
        [JsonConverter(typeof(NetworkJsonConverter))]
        public NBitcoin.Network Network { get; internal set; } = NBitcoin.Network.Main;

        [DefaultValue("http://cmekpfcgcdmaegqdsj4x4j6qkdem2jhndnboegwhf3jwr2mubafjl3id.onion/")]
        [JsonProperty(PropertyName = "MainNetBackendUriV3", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string MainNetBackendUriV3 { get; private set; }

        [DefaultValue("http://testwnp3fugjln6vh5vpj7mvq3lkqqwjj3c2aafyu7laxz42kgwh2rad.onion/")]
        [JsonProperty(PropertyName = "TestNetBackendUriV3", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string TestNetBackendUriV3 { get; private set; }

        [DefaultValue("https://wasabiwallet.io/")]
        [JsonProperty(PropertyName = "MainNetFallbackBackendUri", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string MainNetFallbackBackendUri { get; private set; }

        [DefaultValue("https://wasabiwallet.co/")]
        [JsonProperty(PropertyName = "TestNetFallbackBackendUri", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string TestNetFallbackBackendUri { get; private set; }

        [DefaultValue("http://localhost:37127/")]
        [JsonProperty(PropertyName = "RegTestBackendUriV3", DefaultValueHandling = DefaultValueHandling.Populate)]
        public string RegTestBackendUriV3 { get; private set; }

        [DefaultValue(true)]
        [JsonProperty(PropertyName = "UseTor", DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool UseTor { get; internal set; }

        [DefaultValue(false)]
        [JsonProperty(PropertyName = "StartLocalBitcoinCoreOnStartup", DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool StartLocalBitcoinCoreOnStartup { get; internal set; }

        [DefaultValue(true)]
        [JsonProperty(PropertyName = "StopLocalBitcoinCoreOnShutdown", DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool StopLocalBitcoinCoreOnShutdown { get; internal set; }

        [JsonProperty(PropertyName = "LocalBitcoinCoreDataDir")]
        public string LocalBitcoinCoreDataDir { get; internal set; } = EnvironmentHelpers.TryGetDefaultBitcoinCoreDataDir() ?? "";

        [JsonProperty(PropertyName = "TorSocks5EndPoint")]
        [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultTorSocksPort)]
        public EndPoint TorSocks5EndPoint { get; internal set; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultTorSocksPort);

        [JsonProperty(PropertyName = "MainNetBitcoinP2pEndPoint")]
        [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultMainNetBitcoinP2pPort)]
        public EndPoint MainNetBitcoinP2pEndPoint { get; internal set; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultMainNetBitcoinP2pPort);

        [JsonProperty(PropertyName = "TestNetBitcoinP2pEndPoint")]
        [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultTestNetBitcoinP2pPort)]
        public EndPoint TestNetBitcoinP2pEndPoint { get; internal set; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultTestNetBitcoinP2pPort);

        [JsonProperty(PropertyName = "RegTestBitcoinP2pEndPoint")]
        [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultRegTestBitcoinP2pPort)]
        public EndPoint RegTestBitcoinP2pEndPoint { get; internal set; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultRegTestBitcoinP2pPort);

        [DefaultValue(DefaultMixUntilAnonymitySet)]
        [JsonProperty(PropertyName = "MixUntilAnonymitySet", DefaultValueHandling = DefaultValueHandling.Populate)]
        public int MixUntilAnonymitySet
        {
            get => _mixUntilAnonymitySet;
            internal set
            {
                if (RaiseAndSetIfChanged(ref _mixUntilAnonymitySet, value))
                {
                    if (ServiceConfiguration != default)
                    {
                        ServiceConfiguration.MixUntilAnonymitySet = value.ToString();
                    }
                }
            }
        }

        [DefaultValue(DefaultPrivacyLevelSome)]
        [JsonProperty(PropertyName = "PrivacyLevelSome", DefaultValueHandling = DefaultValueHandling.Populate)]
        public int PrivacyLevelSome
        {
            get => _privacyLevelSome;
            internal set
            {
                if (_privacyLevelSome != value)
                {
                    _privacyLevelSome = value;
                    if (ServiceConfiguration != default)
                    {
                        ServiceConfiguration.PrivacyLevelSome = value;
                    }
                }
            }
        }

        [DefaultValue(DefaultPrivacyLevelFine)]
        [JsonProperty(PropertyName = "PrivacyLevelFine", DefaultValueHandling = DefaultValueHandling.Populate)]
        public int PrivacyLevelFine
        {
            get => _privacyLevelFine;
            internal set
            {
                if (_privacyLevelFine != value)
                {
                    _privacyLevelFine = value;
                    if (ServiceConfiguration != default)
                    {
                        ServiceConfiguration.PrivacyLevelFine = value;
                    }
                }
            }
        }

        [DefaultValue(DefaultPrivacyLevelStrong)]
        [JsonProperty(PropertyName = "PrivacyLevelStrong", DefaultValueHandling = DefaultValueHandling.Populate)]
        public int PrivacyLevelStrong
        {
            get => _privacyLevelStrong;
            internal set
            {
                if (_privacyLevelStrong != value)
                {
                    _privacyLevelStrong = value;
                    if (ServiceConfiguration != default)
                    {
                        ServiceConfiguration.PrivacyLevelStrong = value;
                    }
                }
            }
        }

        [JsonProperty(PropertyName = "DustThreshold")]
        [JsonConverter(typeof(MoneyBtcJsonConverter))]
        public Money DustThreshold { get; internal set; } = DefaultDustThreshold;

        private Uri _backendUri = null;
        private Uri _fallbackBackendUri;

        public ServiceConfiguration ServiceConfiguration { get; private set; }

        public Uri GetCurrentBackendUri()
        {
            if (TorProcessManager.RequestFallbackAddressUsage)
            {
                return GetFallbackBackendUri();
            }

            if (_backendUri != null)
            {
                return _backendUri;
            }

            if (Network == NBitcoin.Network.Main)
            {
                _backendUri = new Uri(MainNetBackendUriV3);
            }
            else if (Network == NBitcoin.Network.TestNet)
            {
                _backendUri = new Uri(TestNetBackendUriV3);
            }
            else if (Network == NBitcoin.Network.RegTest)
            {
                _backendUri = new Uri(RegTestBackendUriV3);
            }
            else
            {
                throw new NotSupportedNetworkException(Network);
            }

            return _backendUri;
        }

        public Uri GetFallbackBackendUri()
        {
            if (_fallbackBackendUri != null)
            {
                return _fallbackBackendUri;
            }

            if (Network == NBitcoin.Network.Main)
            {
                _fallbackBackendUri = new Uri(MainNetFallbackBackendUri);
            }
            else if (Network == NBitcoin.Network.TestNet)
            {
                _fallbackBackendUri = new Uri(TestNetFallbackBackendUri);
            }
            else if (Network == NBitcoin.Network.RegTest)
            {
                _fallbackBackendUri = new Uri(RegTestBackendUriV3);
            }
            else
            {
                throw new NotSupportedNetworkException(Network);
            }

            return _fallbackBackendUri;
        }

        public EndPoint GetBitcoinP2pEndPoint()
        {
            if (Network == NBitcoin.Network.Main)
            {
                return MainNetBitcoinP2pEndPoint;
            }
            else if (Network == NBitcoin.Network.TestNet)
            {
                return TestNetBitcoinP2pEndPoint;
            }
            else if (Network == NBitcoin.Network.RegTest)
            {
                return RegTestBitcoinP2pEndPoint;
            }
            else
            {
                throw new NotSupportedNetworkException(Network);
            }
        }

        public const string FILENAME = "Config.json";

        public Config(IDataDirProvider dataDirProvider)
            : base(Path.Combine(dataDirProvider.Get(), FILENAME))
        {
            ServiceConfiguration = new ServiceConfiguration(MixUntilAnonymitySet.ToString(), PrivacyLevelSome, PrivacyLevelFine, PrivacyLevelStrong, GetBitcoinP2pEndPoint(), DustThreshold);
        }

        public Config(string filePath) : base(filePath)
        {
            ServiceConfiguration = new ServiceConfiguration(MixUntilAnonymitySet.ToString(), PrivacyLevelSome, PrivacyLevelFine, PrivacyLevelStrong, GetBitcoinP2pEndPoint(), DustThreshold);
        }

        /// <inheritdoc />
        public override void LoadFile()
        {
            base.LoadFile();

            ServiceConfiguration = new ServiceConfiguration(MixUntilAnonymitySet.ToString(), PrivacyLevelSome, PrivacyLevelFine, PrivacyLevelStrong, GetBitcoinP2pEndPoint(), DustThreshold);

            // Just debug convenience.
            _backendUri = GetCurrentBackendUri();
        }

        public void SetP2PEndpoint(EndPoint endPoint)
        {
            if (Network == NBitcoin.Network.Main)
            {
                MainNetBitcoinP2pEndPoint = endPoint;
            }
            else if (Network == NBitcoin.Network.TestNet)
            {
                TestNetBitcoinP2pEndPoint = endPoint;
            }
            else if (Network == NBitcoin.Network.RegTest)
            {
                RegTestBitcoinP2pEndPoint = endPoint;
            }
            else
            {
                throw new NotSupportedNetworkException(Network);
            }
        }

        public EndPoint GetP2PEndpoint()
        {
            if (Network == NBitcoin.Network.Main)
            {
                return MainNetBitcoinP2pEndPoint;
            }
            else if (Network == NBitcoin.Network.TestNet)
            {
                return TestNetBitcoinP2pEndPoint;
            }
            else if (Network == NBitcoin.Network.RegTest)
            {
                return RegTestBitcoinP2pEndPoint;
            }
            else
            {
                throw new NotSupportedNetworkException(Network);
            }
        }

        public static int GetNormalizeAnonSet(Config config)
        {
            config.LoadOrCreateDefaultFile();

            // MixUntilAnonymitySet sanity check.
            if (config.MixUntilAnonymitySet != config.PrivacyLevelFine &&
                config.MixUntilAnonymitySet != config.PrivacyLevelSome &&
                config.MixUntilAnonymitySet != config.PrivacyLevelStrong)
            {
                if (config.MixUntilAnonymitySet < config.PrivacyLevelSome)
                {
                    return config.PrivacyLevelSome;
                }
                else if (config.MixUntilAnonymitySet < config.PrivacyLevelFine)
                {
	                return  config.PrivacyLevelFine;
                }
                else
                {
	                return  config.PrivacyLevelStrong;
                }
            }

            return config.MixUntilAnonymitySet;
        }

        protected override bool TryEnsureBackwardsCompatibility(string jsonString)
        {
            try
            {
                var jsObject = JsonConvert.DeserializeObject<JObject>(jsonString);
                bool saveIt = false;

                var torHost = jsObject.Value<string>("TorHost");
                var torSocks5Port = jsObject.Value<int?>("TorSocks5Port");
                var mainNetBitcoinCoreHost = jsObject.Value<string>("MainNetBitcoinCoreHost");
                var mainNetBitcoinCorePort = jsObject.Value<int?>("MainNetBitcoinCorePort");
                var testNetBitcoinCoreHost = jsObject.Value<string>("TestNetBitcoinCoreHost");
                var testNetBitcoinCorePort = jsObject.Value<int?>("TestNetBitcoinCorePort");
                var regTestBitcoinCoreHost = jsObject.Value<string>("RegTestBitcoinCoreHost");
                var regTestBitcoinCorePort = jsObject.Value<int?>("RegTestBitcoinCorePort");

                if (torHost != null)
                {
                    int port = torSocks5Port ?? Constants.DefaultTorSocksPort;

                    if (EndPointParser.TryParse(torHost, port, out EndPoint ep))
                    {
                        TorSocks5EndPoint = ep;
                        saveIt = true;
                    }
                }

                if (mainNetBitcoinCoreHost != null)
                {
                    int port = mainNetBitcoinCorePort ?? Constants.DefaultMainNetBitcoinP2pPort;

                    if (EndPointParser.TryParse(mainNetBitcoinCoreHost, port, out EndPoint ep))
                    {
                        MainNetBitcoinP2pEndPoint = ep;
                        saveIt = true;
                    }
                }

                if (testNetBitcoinCoreHost != null)
                {
                    int port = testNetBitcoinCorePort ?? Constants.DefaultTestNetBitcoinP2pPort;

                    if (EndPointParser.TryParse(testNetBitcoinCoreHost, port, out EndPoint ep))
                    {
                        TestNetBitcoinP2pEndPoint = ep;
                        saveIt = true;
                    }
                }

                if (regTestBitcoinCoreHost != null)
                {
                    int port = regTestBitcoinCorePort ?? Constants.DefaultRegTestBitcoinP2pPort;

                    if (EndPointParser.TryParse(regTestBitcoinCoreHost, port, out EndPoint ep))
                    {
                        RegTestBitcoinP2pEndPoint = ep;
                        saveIt = true;
                    }
                }

                return saveIt;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Backwards compatibility couldn't be ensured.");
                Logger.LogInfo(ex);
                return false;
            }
        }
    }
}
