using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WpfWidgets.Views
{
    public partial class GeneralSettingsView : System.Windows.Controls.UserControl
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ChronosWidgets";
        private const string OldAppName = "WindowsWidgets";
        private bool _isInitializing = true;

        public GeneralSettingsView()
        {
            InitializeComponent();
            
            // Populate theme dropdown
            ThemeSelector.ItemsSource = new List<string> { "Light", "Dark", "System" };
            
            CheckStartupStatus();
            RefreshAccountDisplay();
            
            // Load theme value from config
            ThemeSelector.SelectedItem = WidgetConfig.Current.ClockTheme;
            
            // Load opacity value from config
            OpacitySlider.Value = WidgetConfig.Current.WidgetOpacity * 100;
            OpacityValueText.Text = $"{(int)OpacitySlider.Value}%";
            
            // Load blur value from config
            BlurToggle.IsChecked = WidgetConfig.Current.WidgetBlurEnabled;
            
            _isInitializing = false;
        }

        private void CheckStartupStatus()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
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
                                    using (RegistryKey? writeKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                                    {
                                        if (writeKey != null)
                                        {
                                            writeKey.SetValue(AppName, oldValue);
                                            writeKey.DeleteValue(OldAppName, false);
                                            value = oldValue;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to migrate startup registry key: {ex.Message}");
                                }

                                try
                                {
                                    using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                                    {
                                        if (approvedKey != null)
                                        {
                                            object? approvedValue = approvedKey.GetValue(OldAppName);
                                            if (approvedValue != null)
                                            {
                                                approvedKey.SetValue(AppName, approvedValue);
                                                approvedKey.DeleteValue(OldAppName, false);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to migrate StartupApproved registry key: {ex.Message}");
                                }
                            }
                        }
                        if (value == null)
                        {
                            StartupToggle.IsChecked = false;
                        }
                        else
                        {
                            bool isEnabled = true;
                            try
                            {
                                using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                                {
                                    if (approvedKey != null)
                                    {
                                        byte[]? approvedBytes = approvedKey.GetValue(AppName) as byte[];
                                        if (approvedBytes != null && approvedBytes.Length > 0)
                                        {
                                            if ((approvedBytes[0] & 1) != 0)
                                            {
                                                isEnabled = false;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to check StartupApproved key: {ex.Message}");
                            }
                            StartupToggle.IsChecked = isEnabled;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to read startup registry key: {ex.Message}");
            }
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enableStartup = StartupToggle.IsChecked ?? false;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        if (enableStartup)
                        {
                            // Get WpfWidgets.exe file path in output directory
                            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfWidgets.exe");
                            key.SetValue(AppName, $"\"{exePath}\"");

                            try
                            {
                                using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
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
                                System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to update StartupApproved key for enable: {ex.Message}");
                            }
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);

                            try
                            {
                                using (RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
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
                                System.Diagnostics.Debug.WriteLine($"[GeneralSettings] Failed to clean StartupApproved key: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to change Windows startup state: {ex.Message}", "Registry Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            string? selectedTheme = ThemeSelector.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedTheme)) return;

            WidgetConfig.Current.ClockTheme = selectedTheme;
            WidgetConfig.Save();

            // Apply theme changes globally
            DashboardWindow.ApplyTheme(selectedTheme);
        }

        /// <summary>
        /// Refreshes settings UI controls from the active configuration.
        /// </summary>
        public void RefreshSettings()
        {
            _isInitializing = true;
            CheckStartupStatus();
            RefreshAccountDisplay();
            ThemeSelector.SelectedItem = WidgetConfig.Current.ClockTheme;
            OpacitySlider.Value = WidgetConfig.Current.WidgetOpacity * 100;
            OpacityValueText.Text = $"{(int)OpacitySlider.Value}%";
            BlurToggle.IsChecked = WidgetConfig.Current.WidgetBlurEnabled;
            _isInitializing = false;
        }

        private void RefreshAccountDisplay()
        {
            string userId = WidgetConfig.Current.CalendarUserId;
            AccountIdText.Text = string.IsNullOrEmpty(userId) ? "Not signed in" : userId;
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to sign out?\nThe app will restart and you will need to sign in again.",
                "Sign Out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Clear credentials
            var app = System.Windows.Application.Current as App;
            app?.SyncService?.SignOut();

            // Restart the app so the LoginWindow is shown on next startup
            string exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfWidgets.exe");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true
            });
            System.Windows.Application.Current.Shutdown();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;

            double opacityVal = OpacitySlider.Value;
            OpacityValueText.Text = $"{(int)opacityVal}%";

            WidgetConfig.Current.WidgetOpacity = opacityVal / 100.0;
            WidgetConfig.Save();

            // Apply opacity instantly
            var app = System.Windows.Application.Current as App;
            app?.ApplyWidgetOpacity();
        }

        private void BlurToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool enableBlur = BlurToggle.IsChecked ?? false;
            WidgetConfig.Current.WidgetBlurEnabled = enableBlur;
            WidgetConfig.Save();

            // Apply blur instantly
            var app = System.Windows.Application.Current as App;
            app?.ApplyWidgetBlur();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.5));
                e.Handled = true;
            }
        }
    }
}
