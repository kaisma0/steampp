using SteamPP.ViewModels;
using SteamPP.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SteamPP.Views
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly ThemeService _themeService;

        public MainWindow(MainViewModel viewModel, SettingsService settingsService, ThemeService themeService)
        {
            InitializeComponent();
            DataContext = viewModel;
            _settingsService = settingsService;
            _themeService = themeService;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SourceInitialized += MainWindow_SourceInitialized;

            // Subscribe to theme changes
            _themeService.ThemeChanged += RefreshTitleBarColor;

            // Restore window size
            var settings = _settingsService.LoadSettings();
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;

            if (settings.WindowLeft.HasValue) Left = settings.WindowLeft.Value;
            if (settings.WindowTop.HasValue) Top = settings.WindowTop.Value;

            if (settings.StartMinimized)
            {
                WindowState = WindowState.Minimized;
            }
            else
            {
                WindowState = settings.WindowState;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
             RefreshTitleBarColor();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save window size
            var settings = _settingsService.LoadSettings();
            
            settings.WindowState = WindowState;

            if (WindowState == WindowState.Normal)
            {
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }
            else
            {
                settings.WindowWidth = RestoreBounds.Width;
                settings.WindowHeight = RestoreBounds.Height;
                settings.WindowLeft = RestoreBounds.Left;
                settings.WindowTop = RestoreBounds.Top;
            }

            _settingsService.SaveSettings(settings);

            // Check if we should minimize to tray instead of closing
            if (settings.MinimizeToTray)
            {
                e.Cancel = true;
                var app = Application.Current as App;
                var trayService = app?.GetTrayIconService();
                trayService?.ShowInTray();
            }
        }

        private void MainWindow_SourceInitialized(object? sender, System.EventArgs e)
        {
            RefreshTitleBarColor();
        }

        private void MainWindow_StateChanged(object? sender, System.EventArgs e)
        {
            var settings = _settingsService.LoadSettings();
            settings.WindowState = WindowState;
        }

        private void RefreshTitleBarColor()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr handle = helper.Handle;

            if (handle == IntPtr.Zero) return;

            // Get colors from current theme resources
            var backgroundBrush = Application.Current.Resources["PrimaryDarkBrush"] as System.Windows.Media.SolidColorBrush;
            var foregroundBrush = Application.Current.Resources["TextPrimaryBrush"] as System.Windows.Media.SolidColorBrush;

            if (backgroundBrush != null)
            {
                int color = (backgroundBrush.Color.B << 16) | (backgroundBrush.Color.G << 8) | backgroundBrush.Color.R;
                DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, ref color, sizeof(int));
            }

            if (foregroundBrush != null)
            {
                int color = (foregroundBrush.Color.B << 16) | (foregroundBrush.Color.G << 8) | foregroundBrush.Color.R;
                DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, ref color, sizeof(int));
            }

            // Set dark/light mode for system buttons based on background brightness
            // Assuming dark theme for now as per default resources, but can be dynamic
            int useImmersiveDarkMode = 1; // True
            DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }

        private enum DWMWINDOWATTRIBUTE : uint
        {
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_CAPTION_COLOR = 35,
            DWMWA_TEXT_COLOR = 36
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);
    }
}
