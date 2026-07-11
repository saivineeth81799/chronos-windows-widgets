using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace WpfWidgets
{
    public partial class App : Application
    {
        private NotifyIcon? _notifyIcon;
        private ClockWidgetWindow? _clockWindow;
        private CalendarWidgetWindow? _calendarWindow;
        private TasksWidgetWindow? _tasksWindow;
        private WeatherWidgetWindow? _weatherWindow;
        private DashboardWindow? _dashboardWindow;
        public WeatherWidgetWindow? WeatherWidgetWindow => _weatherWindow;
        public FirebaseSyncService? SyncService { get; private set; }
        private string _lastIconName = "";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Load application config
            WidgetConfig.Load();

            // 2. Initialize and apply initial theme resource dictionary
            DashboardWindow.ApplyTheme(WidgetConfig.Current.ClockTheme);

            // 3. Initialize Firebase sync service early (needed by LoginWindow)
            SyncService = new FirebaseSyncService();
            SyncService.EventsUpdated += OnCalendarEventsUpdated;
            SyncService.TasksUpdated += OnTasksUpdated;

            // 4. Auth gate: if no stored token, show login screen first
            bool isSignedIn = !string.IsNullOrEmpty(WidgetConfig.Current.GetRefreshToken());
            if (!isSignedIn)
            {
                var loginWindow = new LoginWindow(SyncService);
                // ShowDialog blocks until the window is closed
                loginWindow.ShowDialog();

                // If the user closed the window without signing in, shut down
                if (!loginWindow.LoginSucceeded)
                {
                    Shutdown();
                    return;
                }
            }

            // 5. User is authenticated — create and show widgets
            _clockWindow = new ClockWidgetWindow();
            _calendarWindow = new CalendarWidgetWindow();
            _tasksWindow = new TasksWidgetWindow();
            _weatherWindow = new WeatherWidgetWindow();
            _dashboardWindow = new DashboardWindow();
 
            MainWindow = _clockWindow;
            ApplyWidgetOpacity();
            if (WidgetConfig.Current.ClockEnabled)
            {
                _clockWindow.Show();
            }
 
            // 6. Start background calendar sync
            SyncService.Initialize(); // starts timer + first sync
 
            // 7. Show calendar widget if enabled
            if (WidgetConfig.Current.CalendarEnabled)
            {
                _calendarWindow.Show();
            }
            
            // Show tasks widget if enabled
            if (WidgetConfig.Current.TasksEnabled)
            {
                _tasksWindow.Show();
            }

            // Show weather widget if enabled
            if (WidgetConfig.Current.WeatherEnabled)
            {
                _weatherWindow.Show();
            }

            // 8. Set up system tray icon
            InitializeSystemTray();

            // 9. Set up dynamic Windows OS Theme Listener
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            // 10. Force initial icon update matching OS theme
            UpdateTrayAndWindowIcons();
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application, // Default fallback
                Text = "Chronos Widgets Manager",
                Visible = true
            };

            // Set up Context Menu
            var contextMenu = new ContextMenuStrip();

            var settingsItem = new ToolStripMenuItem("Settings Dashboard", null, (s, e) => ShowDashboard());
            contextMenu.Items.Add(settingsItem);

            var lockItem = new ToolStripMenuItem("Lock Position", null, (s, e) => ToggleLockPosition());
            lockItem.Checked = WidgetConfig.Current.ClockLocked;
            contextMenu.Items.Add(lockItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit App", null, (s, e) => ExitApp());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double click opens the settings dashboard
            _notifyIcon.DoubleClick += (s, e) => ShowDashboard();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Windows theme updates fall under General user preference changes
            if (e.Category == UserPreferenceCategory.General)
            {
                // Force UI dispatcher sync so icon updates don't throw thread errors
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTrayAndWindowIcons();

                    // If widget is set to follow system theme, refresh theme resource dictionaries
                    if (WidgetConfig.Current.ClockTheme == "System")
                    {
                        DashboardWindow.ApplyTheme("System");
                    }
                }));
            }
        }

        /// <summary>
        /// Reads registry settings to detect if Windows is using a Light Taskbar Theme.
        /// </summary>
        private static bool IsSystemInLightTheme()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object? value = key.GetValue("SystemUsesLightTheme");
                        if (value is int i)
                        {
                            return i == 1; // 1 = Light Taskbar mode
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Failed to read system theme from registry: {ex.Message}");
            }
            return false; // Default to dark mode (white icon) if registry read fails
        }

        /// <summary>
        /// Reads registry settings to detect if Windows applications are configured to use a Light Theme.
        /// </summary>
        public static bool IsSystemInAppsLightTheme()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object? value = key.GetValue("AppsUseLightTheme");
                        if (value is int i)
                        {
                            return i == 1; // 1 = Apps Light Theme
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Failed to read apps light theme from registry: {ex.Message}");
            }
            return false; // Default to dark if registry read fails
        }

        /// <summary>
        /// Updates both the System Tray and all window taskbar icons based on the active Windows OS theme.
        /// </summary>
        public void UpdateTrayAndWindowIcons()
        {
            if (_notifyIcon == null) return;

            bool isSystemLight = IsSystemInLightTheme();
            LogHelper.Log($"[App] IsSystemInLightTheme: {isSystemLight}");
            // Light taskbar requires dark icon (app-icon-light.ico)
            // Dark taskbar requires light icon (app-icon-dark.ico)
            string iconName = isSystemLight ? "app-icon-light.ico" : "app-icon-dark.ico";

            if (iconName == _lastIconName)
            {
                LogHelper.Log($"[App] Icon name '{iconName}' matches cached, skipping reload.");
                return;
            }
            LogHelper.Log($"[App] Setting tray and window icons to: {iconName}");
            _lastIconName = iconName;

            // 1. Update the System Tray Icon
            try
            {
                var resourceUri = new Uri($"pack://application:,,,/{iconName}", UriKind.Absolute);
                var streamInfo = GetResourceStream(resourceUri);
                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        var oldIcon = _notifyIcon.Icon;
                        _notifyIcon.Icon = new Icon(stream);
                        if (oldIcon != null && oldIcon != SystemIcons.Application)
                        {
                            oldIcon.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Exception setting tray icon: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Failed to update tray icon: {ex.Message}");
            }

            // 2. Update Window Icons (Taskbar Icons for open windows)
            try
            {
                var resourceUri = new Uri($"pack://application:,,,/{iconName}", UriKind.Absolute);
                var bitmap = System.Windows.Media.Imaging.BitmapFrame.Create(resourceUri);

                // We only update the Dashboard taskbar icon.
                // We DO NOT update _mainWindow.Icon because changing Icon at runtime in WPF 
                // forces a native handle recreation, which destroys the low-level Win32 desktop attachment.
                if (_dashboardWindow != null)
                {
                    _dashboardWindow.Icon = bitmap;
                    LogHelper.Log("[App] DashboardWindow taskbar icon set successfully.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Exception setting dashboard icon: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Failed to update window taskbar icons: {ex.Message}");
            }
        }

        private void ShowDashboard()
        {
            if (_dashboardWindow == null)
            {
                _dashboardWindow = new DashboardWindow();
            }

            // Sync settings sub-views before showing
            _dashboardWindow.SyncSettings();

            // Refresh the startup warning banner visibility state on show
            _dashboardWindow.CheckStartupBanner();

            _dashboardWindow.Show();
            _dashboardWindow.Activate();
        }

        private void ToggleLockPosition()
        {
            bool isLocked = !WidgetConfig.Current.ClockLocked;
            WidgetConfig.Current.ClockLocked = isLocked;
            WidgetConfig.Save();

            // Update UI checkbox in context menu if initialized
            if (_notifyIcon?.ContextMenuStrip != null)
            {
                var lockItem = _notifyIcon.ContextMenuStrip.Items[1] as ToolStripMenuItem;
                if (lockItem != null)
                {
                    lockItem.Checked = isLocked;
                }
            }

            // Sync with settings dashboard if visible
            if (_dashboardWindow != null && _dashboardWindow.IsVisible)
            {
                _dashboardWindow.SyncSettings();
            }

            // Apply to main clock window
            _clockWindow?.ApplyLockState();
        }

        private void ExitApp()
        {
            Shutdown();
        }

        /// <summary>
        /// Shows or hides the clock widget dynamically from settings toggles.
        /// </summary>
        public void SetClockWidgetVisibility(bool isVisible)
        {
            if (_clockWindow == null) return;
 
            try
            {
                if (isVisible)
                {
                    _clockWindow.StartClock();
                    _clockWindow.Show();
                    LogHelper.Log("[App] Clock Widget visibility set to: Visible");
                }
                else
                {
                    _clockWindow.StopClock();
                    _clockWindow.Hide();
                    LogHelper.Log("[App] Clock Widget visibility set to: Hidden");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Failed to change clock widget visibility: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Unsubscribe from Windows theme updates to prevent memory leaks
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            // Stop background calendar sync
            SyncService?.StopBackgroundSync();

            // Clean up tray resources to prevent ghost taskbar icons
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            base.OnExit(e);
        }

        private void OnCalendarEventsUpdated(List<CalendarEvent> events)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _calendarWindow?.UpdateEventsList(events);
            }));
        }

        private void OnTasksUpdated(List<WidgetTaskItem> tasks)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _tasksWindow?.UpdateTasksList(tasks);
            }));
        }

        private void UpdateSyncServiceState()
        {
            if (SyncService == null) return;
            bool isSyncNeeded = WidgetConfig.Current.CalendarEnabled || WidgetConfig.Current.TasksEnabled;
            if (isSyncNeeded)
            {
                SyncService.StartBackgroundSync();
            }
            else
            {
                SyncService.StopBackgroundSync();
            }
        }
        
        /// <summary>
        /// Shows or hides the calendar widget dynamically from settings toggles.
        /// </summary>
        public void SetCalendarWidgetVisibility(bool isVisible)
        {
            if (_calendarWindow == null) return;
            try
            {
                if (isVisible)
                {
                    _calendarWindow.Show();
                    LogHelper.Log("[App] Calendar Widget visibility set to: Visible");
                }
                else
                {
                    _calendarWindow.Hide();
                    LogHelper.Log("[App] Calendar Widget visibility set to: Hidden");
                }
                UpdateSyncServiceState();
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Failed to change calendar widget visibility: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows or hides the tasks widget dynamically from settings toggles.
        /// </summary>
        public void SetTasksWidgetVisibility(bool isVisible)
        {
            if (_tasksWindow == null) return;
            try
            {
                if (isVisible)
                {
                    _tasksWindow.Show();
                    LogHelper.Log("[App] Tasks Widget visibility set to: Visible");
                }
                else
                {
                    _tasksWindow.Hide();
                    LogHelper.Log("[App] Tasks Widget visibility set to: Hidden");
                }
                UpdateSyncServiceState();
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Failed to change tasks widget visibility: {ex.Message}");
            }
        }
 
        /// <summary>
        /// Shows or hides the weather widget dynamically from settings toggles.
        /// </summary>
        public void SetWeatherWidgetVisibility(bool isVisible)
        {
            if (_weatherWindow == null) return;
            try
            {
                if (isVisible)
                {
                    _weatherWindow.StartWeatherTimer();
                    _weatherWindow.Show();
                    LogHelper.Log("[App] Weather Widget visibility set to: Visible");
                }
                else
                {
                    _weatherWindow.StopWeatherTimer();
                    _weatherWindow.Hide();
                    LogHelper.Log("[App] Weather Widget visibility set to: Hidden");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[App] Failed to change weather widget visibility: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the current lock state to the calendar widget.
        /// </summary>
        public void ApplyCalendarLockState()
        {
            _calendarWindow?.ApplyLockState();
        }

        /// <summary>
        /// Applies the current lock state to the tasks widget.
        /// </summary>
        public void ApplyTasksLockState()
        {
            _tasksWindow?.ApplyLockState();
        }

        /// <summary>
        /// Instantly updates the opacity of the widget background brush while keeping text opaque.
        /// </summary>
        public void ApplyWidgetOpacity()
        {
            double opacity = WidgetConfig.Current.WidgetOpacity;
            
            // Revert window-level opacity to full visibility
            if (_clockWindow != null) _clockWindow.Opacity = 1.0;
            if (_calendarWindow != null) _calendarWindow.Opacity = 1.0;
            if (_tasksWindow != null) _tasksWindow.Opacity = 1.0;
            if (_weatherWindow != null) _weatherWindow.Opacity = 1.0;

            // Find the WidgetBackground color from merged theme dictionaries
            System.Windows.Media.Color baseColor = System.Windows.Media.Colors.Transparent;
            foreach (var dict in System.Windows.Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Contains("WidgetBackground"))
                {
                    if (dict["WidgetBackground"] is SolidColorBrush themeBrush)
                    {
                        baseColor = themeBrush.Color;
                        break;
                    }
                }
            }

            // Fallback to Dark theme background color if not found
            if (baseColor == System.Windows.Media.Colors.Transparent)
            {
                baseColor = System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x1A);
            }

            // Update WidgetBackground resource dynamically
            var newBrush = new SolidColorBrush(baseColor) { Opacity = opacity };
            System.Windows.Application.Current.Resources["WidgetBackground"] = newBrush;
            LogHelper.Log($"[App] Widget background opacity applied: {opacity:F2} using base color: {baseColor}");
        }
    }

    /// <summary>
    /// Writes log messages to both the Console and a persistent debug.log file in the output folder.
    /// </summary>
    public static class LogHelper
    {
        private static readonly string LogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        private static readonly object LogLock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    System.IO.File.AppendAllText(LogPath, formatted + Environment.NewLine);
                    Console.WriteLine(formatted);
                }
            }
            catch
            {
                // Fallback to console if file is locked
                Console.WriteLine($"[LogHelper] {message}");
            }
        }
    }
}
