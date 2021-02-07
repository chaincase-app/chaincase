using System;
using System.Drawing;

namespace Chaincase.Common.Contracts
{
    public interface IThemeManager
    {
        public bool IsDarkTheme();

        public void SubscribeToThemeChanged(Action handler);

        public void SetUserTheme(string theme);
    }
}
