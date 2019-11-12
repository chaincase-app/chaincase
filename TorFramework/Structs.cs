using System.Runtime.InteropServices;

namespace TorFramework
{

	//	Binding Analysis:
	//  Automated binding is complete, but there are a few APIs which have been flagged with[Verify] attributes.While the entire binding
	//  should be audited for best API design practices, look more closely at APIs with the following Verify attribute hints:

	//  ConstantsInterfaceAssociation (1 instance):

	//	There's no foolproof way to determine with which Objective-C interface an extern variable declaration may be associated. Instances
	//    of these are bound as [Field] properties in a partial interface into a nearby concrete interface to produce a more intuitive API,
	//    possibly eliminating the 'Constants' interface altogether.

	//  PlatformInvoke(4 instances) :

	//	In general P/Invoke bindings are not as correct or complete as Objective-C bindings(at least currently). You may need to fix up the
	//   library name(it defaults to '__Internal') and return/parameter types manually to conform to C calling conventionsfor the target
	//  platform.You may find you don't even want to expose the C API in your binding, but if you do, you'll probably also want to relocate

	//  the definition to a more appropriate class and expose a stronger type-safe wrapper.For P/Invoke guidance, see http://www.mono-project.com/docs/advanced/pinvoke/.


	//Once you have verified a Verify attribute, you should remove it from the binding source code.The presence of Verify attributes
	//intentionally cause build failures.

	//For more information about the Verify attribute hints above, consult the Objective Sharpie documentation by running 'sharpie docs' or
	//visiting the following URL: http://xmn.io/sharpie-docs

	static class CFunctions
	{
		/*
		// extern void TORInstallEventLogging ();
		[DllImport("__Internal")]
		//[Verify(PlatformInvoke)]
		static extern void TORInstallEventLogging();

		// extern void TORInstallEventLoggingCallback (tor_log_cb _Nonnull cb);
		[DllImport("__Internal")]
		//[Verify(PlatformInvoke)]
		static extern unsafe void TORInstallEventLoggingCallback(tor_log_cb* cb);

		// extern void TORInstallTorLogging ();
		[DllImport("__Internal")]
		//[Verify(PlatformInvoke)]
		static extern void TORInstallTorLogging();

		// extern void TORInstallTorLoggingCallback (tor_log_cb _Nonnull cb);
		[DllImport("__Internal")]
		//[Verify(PlatformInvoke)]
		static extern unsafe void TORInstallTorLoggingCallback(tor_log_cb* cb);
		*/
	}
}
