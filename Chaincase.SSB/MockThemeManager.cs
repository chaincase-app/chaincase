using System;
using Chaincase.Common.Contracts;

namespace Chaincase.SSB
{
    public class MockThemeManager : IThemeManager
    {
        public bool IsDarkTheme() => true;

        public void SubscribeToThemeChanged(Action handler)
        {
            return;
        }

		public void SetUserTheme(string theme)
		{
            return;
		}
	}
}
