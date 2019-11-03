using System;
using ObjCRuntime;
using Foundation;
using Xamarin.iOS;

namespace Tor.framework
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

	//  the definition to a more appropriate class and expose a stronger type-safe wrapper.For P/Invoke guidance, see http://www.mono-

	//  project.com/docs/advanced/pinvoke/.


	//Once you have verified a Verify attribute, you should remove it from the binding source code.The presence of Verify attributes
	//intentionally cause build failures.

	//For more information about the Verify attribute hints above, consult the Objective Sharpie documentation by running 'sharpie docs' or
	//visiting the following URL:

	// typedef BOOL (^TORObserverBlock)(NSArray<NSNumber *> * _Nonnull, NSArray<NSData *> * _Nonnull, BOOL * _Nonnull);
	unsafe delegate bool TORObserverBlock(NSNumber[] arg0, NSData[] arg1, bool* arg2);

	[Static]
	//[Verify(ConstantsInterfaceAssociation)]
	partial interface Constants
	{
		// extern const NSErrorDomain _Nonnull TORControllerErrorDomain __attribute__((visibility("default")));
		[Field("TORControllerErrorDomain", "__Internal")]
		NSString TORControllerErrorDomain { get; }
	}

	// @interface TORController : NSObject
	[BaseType(typeof(NSObject))]
	[DisableDefaultCtor]
	interface TORController
	{
		// @property (readonly, copy, nonatomic) NSOrderedSet<NSString *> * _Nonnull events;
		[Export("events", ArgumentSemantic.Copy)]
		NSOrderedSet<NSString> Events { get; }

		// @property (readonly, getter = isConnected, nonatomic) BOOL connected;
		[Export("connected")]
		bool Connected { [Bind("isConnected")] get; }

		// -(instancetype _Nonnull)initWithSocketURL:(NSURL * _Nonnull)url __attribute__((objc_designated_initializer));
		[Export("initWithSocketURL:")]
		[DesignatedInitializer]
		IntPtr Constructor(NSUrl url);

		// -(instancetype _Nonnull)initWithSocketHost:(NSString * _Nonnull)host port:(in_port_t)port __attribute__((objc_designated_initializer));
		[Export("initWithSocketHost:port:")]
		[DesignatedInitializer]
		IntPtr Constructor(string host, ushort port);

		// -(BOOL)connect:(NSError * _Nullable * _Nullable)error;
		[Export("connect:")]
		bool Connect([NullAllowed] out NSError error);

		// -(void)disconnect;
		[Export("disconnect")]
		void Disconnect();

		// -(void)authenticateWithData:(NSData * _Nonnull)data completion:(void (^ _Nullable)(BOOL, NSError * _Nullable))completion;
		[Export("authenticateWithData:completion:")]
		void AuthenticateWithData(NSData data, [NullAllowed] Action<bool, NSError> completion);

		// -(void)resetConfForKey:(NSString * _Nonnull)key completion:(void (^ _Nullable)(BOOL, NSError * _Nullable))completion;
		[Export("resetConfForKey:completion:")]
		void ResetConfForKey(string key, [NullAllowed] Action<bool, NSError> completion);

		// -(void)setConfForKey:(NSString * _Nonnull)key withValue:(NSString * _Nonnull)value completion:(void (^ _Nullable)(BOOL, NSError * _Nullable))completion;
		[Export("setConfForKey:withValue:completion:")]
		void SetConfForKey(string key, string value, [NullAllowed] Action<bool, NSError> completion);

		// -(void)setConfs:(NSArray<NSDictionary *> * _Nonnull)configs completion:(void (^ _Nullable)(BOOL, NSError * _Nullable))completion;
		[Export("setConfs:completion:")]
		void SetConfs(NSDictionary[] configs, [NullAllowed] Action<bool, NSError> completion);

		// -(void)listenForEvents:(NSArray<NSString *> * _Nonnull)events completion:(void (^ _Nullable)(BOOL, NSError * _Nullable))completion;
		[Export("listenForEvents:completion:")]
		void ListenForEvents(string[] events, [NullAllowed] Action<bool, NSError> completion);

		// -(void)getInfoForKeys:(NSArray<NSString *> * _Nonnull)keys completion:(void (^ _Nonnull)(NSArray<NSString *> * _Nonnull))completion;
		[Export("getInfoForKeys:completion:")]
		void GetInfoForKeys(string[] keys, Action<NSArray<NSString>> completion);

		// -(void)getSessionConfiguration:(void (^ _Nonnull)(NSURLSessionConfiguration * _Nullable))completion;
		[Export("getSessionConfiguration:")]
		void GetSessionConfiguration(Action<NSUrlSessionConfiguration> completion);

		// -(void)sendCommand:(NSString * _Nonnull)command arguments:(NSArray<NSString *> * _Nullable)arguments data:(NSData * _Nullable)data observer:(TORObserverBlock _Nonnull)observer;
		[Export("sendCommand:arguments:data:observer:")]
		void SendCommand(string command, [NullAllowed] string[] arguments, [NullAllowed] NSData data, TORObserverBlock observer);

		// -(id _Nonnull)addObserverForCircuitEstablished:(void (^ _Nonnull)(BOOL))block;
		[Export("addObserverForCircuitEstablished:")]
		NSObject AddObserverForCircuitEstablished(Action<bool> block);

		// -(id _Nonnull)addObserverForStatusEvents:(BOOL (^ _Nonnull)(NSString * _Nonnull, NSString * _Nonnull, NSString * _Nonnull, NSDictionary<NSString *,NSString *> * _Nullable))block;
		[Export("addObserverForStatusEvents:")]
		NSObject AddObserverForStatusEvents(Func<NSString, NSString, NSString, NSDictionary<NSString, NSString>, bool> block);

		// -(void)removeObserver:(id _Nullable)observer;
		[Export("removeObserver:")]
		void RemoveObserver([NullAllowed] NSObject observer);
	}

	// @interface TORConfiguration : NSObject
	[BaseType(typeof(NSObject))]
	interface TORConfiguration
	{
		// @property (copy, nonatomic) NSURL * _Nullable dataDirectory;
		[NullAllowed, Export("dataDirectory", ArgumentSemantic.Copy)]
		NSUrl DataDirectory { get; set; }

		// @property (copy, nonatomic) NSURL * _Nullable controlSocket;
		[NullAllowed, Export("controlSocket", ArgumentSemantic.Copy)]
		NSUrl ControlSocket { get; set; }

		// @property (copy, nonatomic) NSURL * _Nullable socksURL;
		[NullAllowed, Export("socksURL", ArgumentSemantic.Copy)]
		NSUrl SocksURL { get; set; }

		// @property (copy, nonatomic) NSNumber * _Nullable cookieAuthentication;
		[NullAllowed, Export("cookieAuthentication", ArgumentSemantic.Copy)]
		NSNumber CookieAuthentication { get; set; }

		// @property (copy, nonatomic) NSDictionary<NSString *,NSString *> * _Null_unspecified options;
		[Export("options", ArgumentSemantic.Copy)]
		NSDictionary<NSString, NSString> Options { get; set; }

		// @property (copy, nonatomic) NSArray<NSString *> * _Null_unspecified arguments;
		[Export("arguments", ArgumentSemantic.Copy)]
		string[] Arguments { get; set; }
	}

	// @interface TORThread : NSThread
	[BaseType(typeof(NSThread))]
	interface TORThread
	{
		// @property (readonly, class) TORThread * _Nullable activeThread;
		[Static]
		[NullAllowed, Export("activeThread")]
		TORThread ActiveThread { get; }

		// -(instancetype _Nonnull)initWithConfiguration:(TORConfiguration * _Nullable)configuration;
		[Export("initWithConfiguration:")]
		IntPtr Constructor([NullAllowed] TORConfiguration configuration);

		// -(instancetype _Nonnull)initWithArguments:(NSArray<NSString *> * _Nullable)arguments __attribute__((objc_designated_initializer));
		[Export("initWithArguments:")]
		[DesignatedInitializer]
		IntPtr Constructor([NullAllowed] string[] arguments);
	}
}

