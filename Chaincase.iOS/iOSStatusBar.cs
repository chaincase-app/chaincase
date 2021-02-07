using Chaincase.Common.Contracts;
using Foundation;
using UIKit;
using Xamarin.Forms;
using Xamarin.Forms.Platform.iOS;

namespace Chaincase.iOS
{
    public class iOSStatusBar : IStatusBar
    {
        public void SetStatusBarColor(string colorHex)
        {
            UINavigationBar.Appearance.BarTintColor = UIColor.Black;
            UINavigationBar.Appearance.TintColor = UIColor.Black;
            UIToolbar.Appearance.BarTintColor = UIColor.Black;
            UIToolbar.Appearance.TintColor = UIColor.Black;
            //UIView statusBar = UIApplication.SharedApplication.ValueForKey(
            //new NSString("statusBar")) as UIView;
            //if (statusBar != null && statusBar.RespondsToSelector(
            //new ObjCRuntime.Selector("setBackgroundColor:")))
            //{
            //    var color = Color.FromHex(colorHex);
            //    statusBar.BackgroundColor = color.ToUIColor();
            //}

   //         if (UIDevice.CurrentDevice.CheckSystemVersion(13, 0))
			//{
   //             UIView statusBar = new UIView(UIApplication.SharedApplication.KeyWindow.WindowScene.StatusBarManager.StatusBarFrame);
   //             statusBar.BackgroundColor = UIStatusBarStyle.DarkContent;
   //             UIApplication.SharedApplication.KeyWindow.AddSubview(statusBar);

			//} else { }
        }
    }
}
