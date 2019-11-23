
using Foundation;
using System;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace TorFramework.Tests
{
	[TestFixture]
	public class TORControllerTests
	{
		private TORConfiguration configuration;

		private void SetUp()
		{
			configuration = new TORConfiguration();
			configuration.CookieAuthentication = new Foundation.NSNumber(true); //@YES
			// This goes contrary to the TARGET_IPHONE_SIMULATOR in iCepa's tests.
			configuration.DataDirectory = new NSUrl(System.IO.Path.GetTempPath());
			configuration.ControlSocket = new NSUrl(NSFileManager.DefaultManager.GetCurrentDirectory());
			configuration.Arguments = new string[] { "--ignore-missing-torrc" };

			TORThread thread = new TORThread(configuration);
			thread.Start();
			NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.5f));
		}

		[Test]
		public void Pass()
		{
			SetUp();
			Assert.True(true);
		}

		[Test]
		public void Fail()
		{
			Assert.False(true);
		}

		[Test]
		[Ignore("another time")]
		public void Ignore()
		{
			Assert.True(false);
		}
	}
}

