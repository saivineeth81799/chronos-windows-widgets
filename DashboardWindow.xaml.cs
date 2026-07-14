using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfWidgets.Views;

namespace WpfWidgets
{
    public partial class DashboardWindow : Window
    {
        // Cached views to make navigation instant
        private GeneralSettingsView? _generalSettingsView;
        private ClockSettingsView? _clockSettingsView;
        private CalendarSettingsView? _calendarSettingsView;
        private TasksSettingsView? _tasksSettingsView;
        private WeatherSettingsView? _weatherSettingsView;
        private PlaceholderSettingsView? _placeholderSettingsView;

        public DashboardWindow()
        {
            InitializeComponent();
            
            // Set initial dynamic view to General Settings
            ShowView("General");

            // Evaluate startup status to determine banner visibility
            CheckStartupBanner();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the dashboard card window
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide(); // Hide instead of closing to preserve state
        }

        private void NavButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton navButton)
            {
                string? category = navButton.Content as string;
                if (!string.IsNullOrEmpty(category))
                {
                    ShowView(category);
                }
            }
        }

        /// <summary>
        /// Mounts the corresponding settings UserControl inside the dashboard content container.
        /// </summary>
        private void ShowView(string category)
        {
            if (ActiveSettingsPanel == null) return;
 
            switch (category)
            {
                case "General":
                    _generalSettingsView ??= new GeneralSettingsView();
                    ActiveSettingsPanel.Content = _generalSettingsView;
                    break;
                case "Clock Widget":
                    _clockSettingsView ??= new ClockSettingsView();
                    ActiveSettingsPanel.Content = _clockSettingsView;
                    break;
                case "Calendar Widget":
                    _calendarSettingsView ??= new CalendarSettingsView();
                    ActiveSettingsPanel.Content = _calendarSettingsView;
                    break;
                case "Tasks Widget":
                    _tasksSettingsView ??= new TasksSettingsView();
                    ActiveSettingsPanel.Content = _tasksSettingsView;
                    break;
                case "Weather Widget":
                    _weatherSettingsView ??= new WeatherSettingsView();
                    ActiveSettingsPanel.Content = _weatherSettingsView;
                    break;
            }
        }

        /// <summary>
        /// Loads the theme resource dictionary and merges it into the Application Resources,
        /// triggering instant style updates across all active windows.
        /// </summary>
        public static void ApplyTheme(string themeName)
        {
            try
            {
                // Resolve "System" theme to the active Windows app theme
                string targetTheme = themeName;
                if (themeName == "System")
                {
                    targetTheme = App.IsSystemInAppsLightTheme() ? "Light" : "Dark";
                    LogHelper.Log($"[Theme] Resolved 'System' theme to: {targetTheme}");
                }

                var uri = new Uri($"/Themes/{targetTheme}.xaml", UriKind.Relative);
                var dict = new ResourceDictionary { Source = uri };

                // Clear previous theme dicts and add the new one
                var mergedDicts = System.Windows.Application.Current.Resources.MergedDictionaries;
                
                // Keep global control styles (first item in App.xaml) but replace the theme dictionary
                // Note: The theme is always index 0 because we merge it first in App.xaml
                if (mergedDicts.Count > 0)
                {
                    mergedDicts.Clear();
                }
                mergedDicts.Add(dict);

                // Apply opacity immediately using the new theme's base color
                var app = System.Windows.Application.Current as App;
                app?.ApplyWidgetOpacity();
                app?.WeatherWidgetWindow?.UpdateTheme();

                System.Diagnostics.Debug.WriteLine($"[Dashboard] Successfully applied theme: {targetTheme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Failed to apply theme '{themeName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the window taskbar icon dynamically when the theme changes.
        /// </summary>
        public void UpdateTaskbarIcon(string themeName)
        {
            try
            {
                string iconName = themeName == "Dark" ? "app-icon-dark.ico" : "app-icon-light.ico";
                var uri = new Uri($"pack://application:,,,/{iconName}", UriKind.Absolute);
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(uri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] Failed to update taskbar icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Synchronizes and updates settings values in all active sub-views.
        /// </summary>
        public void SyncSettings()
        {
            _clockSettingsView?.RefreshSettings();
            _generalSettingsView?.RefreshSettings();
            _calendarSettingsView?.RefreshSettings();
            _tasksSettingsView?.RefreshSettings();
            _weatherSettingsView?.RefreshSettings();
        }

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ChronosWidgets";
        private const string OldAppName = "WindowsWidgets";

        /// <summary>
        /// Checks the Windows Registry to determine if the application is set to start with Windows.
        /// If not, displays a warning banner at the top of the dashboard content pane.
        /// </summary>
        public void CheckStartupBanner()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    if (key != null)
                    {
                        object? value = key.GetValue(AppName);
                        if (value == null)
                        {
                            object? oldValue = key.GetValue(OldAppName);
                            if (oldValue != null)
                            {
                                try
                                {
                                    using (Microsoft.Win32.RegistryKey? writeKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                                    {
                                        if (writeKey != null)
                                        {
                                            writeKey.SetValue(AppName, oldValue);
                                            writeKey.DeleteValue(OldAppName, false);
                                            value = oldValue;
                                            LogHelper.Log("[Dashboard] Migrated startup registry key from WindowsWidgets to ChronosWidgets.");
                                        }
                                    }

                                    try
                                    {
                                        using (Microsoft.Win32.RegistryKey? approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                                        {
                                            if (approvedKey != null)
                                            {
                                                object? approvedValue = approvedKey.GetValue(OldAppName);
                                                if (approvedValue != null)
                                                {
                                                    approvedKey.SetValue(AppName, approvedValue);
                                                    approvedKey.DeleteValue(OldAppName, false);
                                                    LogHelper.Log("[Dashboard] Migrated StartupApproved registry key.");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogHelper.Log($"[Dashboard] Failed to migrate StartupApproved registry key: {ex.Message}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Log($"[Dashboard] Failed to migrate startup registry key: {ex.Message}");
                                }
                            }
                        }
                        bool isStartupEnabled = value != null;

                        // Check StartupApproved key for Task Manager disabled flag
                        if (isStartupEnabled)
                        {
                            try
                            {
                                using (Microsoft.Win32.RegistryKey? approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                                {
                                    if (approvedKey != null)
                                    {
                                        byte[]? approvedBytes = approvedKey.GetValue(AppName) as byte[];
                                        if (approvedBytes != null && approvedBytes.Length > 0)
                                        {
                                            if ((approvedBytes[0] & 1) != 0)
                                            {
                                                isStartupEnabled = false;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Log($"[Dashboard] Failed to read StartupApproved key: {ex.Message}");
                            }
                        }

                        StartupBanner.Visibility = isStartupEnabled ? Visibility.Collapsed : Visibility.Visible;
                        MobileAppBanner.Visibility = isStartupEnabled ? Visibility.Visible : Visibility.Collapsed;
                        LogHelper.Log($"[Dashboard] Registry check completed. Startup enabled: {isStartupEnabled}");
                    }
                    else
                    {
                        StartupBanner.Visibility = Visibility.Visible;
                        MobileAppBanner.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                StartupBanner.Visibility = Visibility.Visible;
                MobileAppBanner.Visibility = Visibility.Collapsed;
                LogHelper.Log($"[Dashboard] Failed to read registry startup status: {ex.Message}");
            }
        }

        private void EnableStartup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfWidgets.exe");
                        key.SetValue(AppName, $"\"{exePath}\"");
                        LogHelper.Log($"[Dashboard] Startup registered successfully: {exePath}");
                    }
                }
                
                // Clear StartupApproved blocker so Windows actually launches it
                try
                {
                    using (Microsoft.Win32.RegistryKey? approvedKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                    {
                        if (approvedKey != null)
                        {
                            approvedKey.DeleteValue(AppName, false);
                            approvedKey.DeleteValue(OldAppName, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"[Dashboard] Failed to clear approvedKey: {ex.Message}");
                }
                
                // Hide banner and show mobile app link
                StartupBanner.Visibility = Visibility.Collapsed;
                MobileAppBanner.Visibility = Visibility.Visible;

                // Sync the active settings sub-views
                SyncSettings();
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[Dashboard] Failed to enable startup registry: {ex.Message}");
                System.Windows.MessageBox.Show($"Failed to enable startup: {ex.Message}", "Registry Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DismissBanner_Click(object sender, RoutedEventArgs e)
        {
            StartupBanner.Visibility = Visibility.Collapsed;
            MobileAppBanner.Visibility = Visibility.Visible;
            LogHelper.Log("[Dashboard] User dismissed the startup banner.");
        }

        private void DownloadMobileApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://play.google.com/store/apps/details?id=com.calmmind.chronos") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[Dashboard] Failed to open mobile app link: {ex.Message}");
            }
        }
    }
}
