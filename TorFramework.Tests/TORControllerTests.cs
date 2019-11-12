using Foundation;
using NUnit.Framework;

namespace TorFramework.Tests
{
	[TestFixture]
	public class TORControllerTests
	{
		[SetUp]
		public void Init()
		{
			//configuration.CookieAuthentication = new NSNumber(true);
			//configuration.Arguments = new[] { "--ignore-missing-torrc" };
		}

		[Test]
		public void Pass()
		{
			Assert.True(true);
		}

		[Test]
		public void Fail()
		{
			TORConfiguration configuration = new TORConfiguration();
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

