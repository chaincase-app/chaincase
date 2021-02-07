using System;
using Chaincase.Common;
using Chaincase.Common.Contracts;

namespace Chaincase.UI.Services
{

	public class UIStateService
	{



		private string _title;
		private string _theme;
		private bool _darkMode;
		protected IThemeManager _themeManager;
		protected Global _global;

		public UIStateService(IThemeManager themeManager, Global global)
		{
			global.Resumed += (s, e) => SetTheme();
			_global = global;
			Theme = _global.UiConfig.Theme;
			_themeManager = themeManager;
			themeManager.SubscribeToThemeChanged(() => SetTheme());
		}

		public string Title
		{
			get => _title;
			set
			{
				_title = value;
				StateChanged?.Invoke();
			}
		}

		public string Theme
		{
			get => _theme;
			set
			{
				_theme = value;
				if (value != _global.UiConfig.Theme)
				{
					_global.UiConfig.Theme = value;
					_global.UiConfig.ToFile();
				}
				SetTheme();
			}
		}

		public void SetTheme()
		{
			switch(Theme)
			{
				case "light":
					DarkMode = false;
					break;
				case "dark":
					DarkMode = true;
					break;
				case "system":
				default:
					DarkMode = _themeManager.IsDarkTheme();
					break;
			}
		}

		public bool DarkMode
		{
			get => _darkMode;
			set
			{
				_darkMode = value;
				StateChanged?.Invoke();
				ThemeChanged?.Invoke();
			}
		}

		public event Action StateChanged;
		public event Action ThemeChanged;
	}
}
