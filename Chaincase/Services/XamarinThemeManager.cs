using System;
using Chaincase.Common.Contracts;
using Xamarin.Forms;

namespace Chaincase.Services
{
    public class XamarinThemeManager : IThemeManager
    {
        public bool IsDarkTheme() => Application.Current.RequestedTheme == OSAppTheme.Dark;

        public void SubscribeToThemeChanged(Action handler)
        {
            Application.Current.RequestedThemeChanged += (s, a) =>
            {
                handler.Invoke();
            };
        }

        public void SetUserTheme(string theme)
        {
            switch(theme)
            {
                case "Dark":
                    Application.Current.UserAppTheme = OSAppTheme.Dark;
                    break;
                case "Light":
                    Application.Current.UserAppTheme = OSAppTheme.Light;
                    break;
                case "System":
                default:
                    Application.Current.UserAppTheme = OSAppTheme.Unspecified;
                    break;
            }
        }
    }
}
