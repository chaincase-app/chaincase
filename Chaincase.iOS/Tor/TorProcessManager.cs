using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoreFoundation;
using Foundation;
using ObjCRuntime;
using TorFramework;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.TorSocks5;
using Xamarin.Forms;
using Nito.AsyncEx;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using Chaincase.Notifications;
using NBitcoin;


[assembly: Dependency(typeof(Chaincase.iOS.Tor.OnionManager))]
namespace Chaincase.iOS.Tor
{
    public interface OnionManagerDelegate
    {
        void TorConnProgress(int progress);

        void TorConnFinished();

        void TorConnDifficulties();
    }

    public class OnionManager : ITorManager
    {

        private NSData Cookie { get; set; }
        public TorState State { get; set; }
        private DispatchBlock initRetry;

        public OnionManager()
        {
            TorSocks5EndPoint = new IPEndPoint(IPAddress.Loopback, 9050);
            TorController = null;
        }

        /// <summary>
        /// If null then it's just a mock, clearnet is used.
        /// </summary>
        public EndPoint TorSocks5EndPoint { get; }

        public string LogFile { get; }

        public static bool RequestFallbackAddressUsage { get; private set; } = false;
        /// <summary>
		/// Used for TorController and TorThread
		/// </summary>
        private readonly AsyncLock mutex = new AsyncLock();

        public TORController TorController { get; private set; }

        private TORThread torThread;

        //public WasabiSynchronizer Synchronizer { get; private set; }
        public BitcoinStore BitcoinStore { get; private set; }
        public Config Config { get; private set; }
        public NBitcoin.Network Network => Config.Network;



        public ITorManager Mock() // Mock, do not use Tor at all for debug.
        {
            return new OnionManager();
        }

		public void Start(bool ensureRunning, string dataDir)
		{
			if (TorSocks5EndPoint is null)
			{
				return;
			}

			if (dataDir == "mock")
			{
				return;
			}
			
            StartTor(null);
			Logger.LogInfo($"Started Tor process with Tor.framework");

			if (ensureRunning)
			{
				Task.Delay(3000).ConfigureAwait(false).GetAwaiter().GetResult(); // dotnet brainfart, ConfigureAwait(false) IS NEEDED HERE otherwise (only on) Manjuro Linux fails, WTF?!!
				if (!IsTorRunningAsync(TorSocks5EndPoint).GetAwaiter().GetResult())
				{
					throw new TorException("Attempted to start Tor, but it is not running.");
				}
				Logger.LogInfo("Tor is running.");
			}
		}

        public async Task StartAsync(bool ensureRunning, string dataDir)
        {
            if (TorSocks5EndPoint is null)
            {
                return;
            }

            if (dataDir == "mock")
            {
                return;
            }

            await StartTor(null);
            Logger.LogInfo($"TorProcessManager.StartAsync(): Started Tor process with Tor.framework");

            if (ensureRunning)
            {
                Task.Delay(3000).ConfigureAwait(false).GetAwaiter().GetResult(); // dotnet brainfart, ConfigureAwait(false) IS NEEDED HERE otherwise (only on) Manjuro Linux fails, WTF?!!
                if (!IsTorRunningAsync(TorSocks5EndPoint).GetAwaiter().GetResult())
                {
                    throw new TorException("Attempted to start Tor, but it is not running.");
                }
                Logger.LogInfo("TorProcessManager.StartAsync(): Tor is running.");
            }
        }

        // port from iOS OnionBrowser


        public async Task StartTor(OnionManagerDelegate managerDelegate)
        {
            // In objective-c this is weak to avoid retain cycle. not sure if we
            // need to replicate cause we got a GC
            var weakDelegate = managerDelegate;

            using (await mutex.LockAsync())
            {
                try
                {
                    if (State == TorState.Started || State == TorState.Connected) return;

                    CancelInitRetry();

                    if (TorController == null)
                        TorController = new TORController("127.0.0.1", 39060);

                    if (State == TorState.None || State == TorState.Stopped || Cookie == null)
                    {
                        try
                        {
                            torThread = new TORThread(this.torBaseConf);
                        }
                        catch (Exception e) { }
                    }
                    torThread?.Start();
                    State = TorState.Started;

                    if (!(TorController?.Connected ?? false))
                    {
                        // do
                        NSError e = null;
                        TorController?.Connect(out e);
                        if (e != null) // faux catch accomodates bound obj-c)
                        {
                            Logger.LogError(e.LocalizedDescription);
                        }
                    }
					// Set up call backs
					Cookie = NSData.FromUrl(torBaseConf.DataDirectory.Append("control_auth_cookie", false));
					TorController?.AuthenticateWithData(Cookie, authWithData);

				}
                catch (Exception e) {
                    Logger.LogError($"TorProcessManager.StartTor(): Failed to start tor {e.Message}");
                }
            }

            initRetry = new DispatchBlock( async() =>
			{
                using (await mutex.LockAsync())
                {
                    Logger.LogDebug("Triggering Tor connection retry.");

                    TorController?.SetConfForKey("DisableNetwork", "1", null);
                    TorController?.SetConfForKey("DisableNetwork", "0", null);

                    // Hint user that they might need to use a bridge.
                    managerDelegate?.TorConnDifficulties();
                }
			});

			// On first load: If Tor hasn't finished bootstrap in 30 seconds,
			// HUP tor once in case we have partially bootstrapped but got stuck.
			//DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, TimeSpan.FromSeconds(15)), initRetry!);
		}

        private async void CircuitEstablished(bool established) {
            NSObject completeObs = null;

            using (await mutex.LockAsync())
            {
                try
                {
                    if (established)
                    {
                        State = TorState.Connected;
                        TorController?.RemoveObserver(completeObs);
                        CancelInitRetry();
                        //weakDelegate?.TorConnFinished();
                        Logger.LogDebug("torProcessManager.CircuitEstablished(): Connection established!");
                    }
                }
                catch (Exception error)
                {
                    Logger.LogError($"TorProcessManager.CircuitEstablished(): Failed to start tor {error.Message}");
                }
            }
        }

  //      private async bool TorEventStatusUpdates(NSString type, NSString severity, NSString action, NSDictionary<NSString, NSString> arguments)
  //      {
  //          using (await mutex.LockAsync())
  //          {
  //              if (type == "STATUS_CLIENT" && action == "BOOTSTRAP")
		//		{
		//			var progress = Int32.Parse(arguments![(NSString)"PROGRESS"]!)!;
		//			Logger.LogDebug(progress.ToString());

		//			//weakDelegate?.TorConnProgress(progress);

		//			if (progress >= 100)
		//			{
		//				//TorController?.RemoveObserver(progressObs);
		//			}

		//			return true;
		//		}

		//		return false;
		//	}

		//}

        private async void authWithData(bool success, NSError error) {

            Logger.LogDebug($"TorController?.AuthenticateWithData(): Authenticate with Data got callbacked");
            if (success) {
                try
                {

                }
                catch (Exception err) {
                    Logger.LogDebug($"TorController.AuthenticateWithData(): Failed to sync filters {err.Message} ");
                }
            }
   //         if (success && TorController.Connected && !torThread.IsCancelled && State == TorState.Started)
   //         {
   //             using (await mutex.LockAsync())
   //             {
   //                 try
   //                 {
   //                     TorController?.AddObserverForCircuitEstablished(CircuitEstablished);

   //                     NSObject progressObs = null;
   //                     //progressObs = TorController?.AddObserverForStatusEvents(TorEventStatusUpdates);
   //                 }
   //                 catch (Exception e) {
   //                     Logger.LogDebug($"torProcessManager.authWithData(): Failed {e.Message}");
   //                 }
   //             }

			//} // if success (authenticate)
            //else
            //{
            //    Logger.LogInfo("Didn't connect to control port.");
            //}
        }

        private TORConfiguration torBaseConf
        {
            get
            {
                string homeDirectory = null;

                if (Runtime.Arch == Arch.SIMULATOR)
                {
                    foreach (string var in new string[] { "IPHONE_SIMULATOR_HOST_HOME", "SIMULATOR_HOST_HOME" })
                    {
                        string val = Environment.GetEnvironmentVariable(var);
                        if (val != null)
                        {
                            homeDirectory = val;
                            break;
                        }
                    }
                }
                else
                {
                    homeDirectory = NSHomeDirectory();
                }

                TORConfiguration configuration = new TORConfiguration();
                configuration.CookieAuthentication = true; //@YES
                configuration.DataDirectory = new NSUrl(Path.GetTempPath());
                configuration.Arguments = new string[] {
                    "--allow-missing-torrc",
                    "--ignore-missing-torrc",
                    "--SocksPort", "127.0.0.1:9050",
                    "--ControlPort", "127.0.0.1:39060",
                };
                return configuration;
            }
        }

        /// <param name="torSocks5EndPoint">Opt out Tor with null.</param>
        public static async Task<bool> IsTorRunningAsync(EndPoint torSocks5EndPoint)
        {
            using var client = new TorSocks5Client(torSocks5EndPoint);
            try
            {
                await client.ConnectAsync().ConfigureAwait(false);
                await client.HandshakeAsync().ConfigureAwait(false);
            }
            catch (ConnectionException)
            {
                return false;
            }
            return true;
        }

        public async Task StopAsync(WasabiSynchronizer Synchronizer )
        {
            if(State != TorState.Started || State != TorState.Connected)
			Logger.LogDebug($"TorProcessManager.StopAsync(): start stopAsync");
			using (await mutex.LockAsync())
			{
				try
                {
                    //var synchronizer = Synchronizer;
                    //if (synchronizer is { } && State == TorState.Connected)
                    //{
                    //    await synchronizer.StopAsync();
                    //    Logger.LogInfo($"Global.OnSleeping():{nameof(Synchronizer)} is stopped.");
                    //}
                    // Under the hood, TORController will SIGNAL SHUTDOWN and set it's channel to null, so
                    // we actually rely on that to stop Tor and reset the state of torController. (we can
                    // SIGNAL SHUTDOWN here, but we can't reset the torController "isConnected" state.)
                    // TODO remove NS observers 
                    TorController?.Disconnect();
					TorController?.Dispose();
					TorController = null;

					torThread.Cancel();
					torThread?.Dispose();
					//torThread = null;
					State = TorState.Stopped;

                    
                }
                catch (Exception error) {
                    Logger.LogError($"TorProcessManager.StopAsync(): Failed to stop tor thread {error}");
                }
			}
		}

        private static string NSHomeDirectory() => Directory.GetParent(NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User).First().Path).FullName;

        // Cancel the connection retry and fail guard.
        private void CancelInitRetry()
        {
            initRetry?.Cancel();
            initRetry = null;
        }
    }
}
