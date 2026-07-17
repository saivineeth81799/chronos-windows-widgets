using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfWidgets.Views
{
    public partial class CalendarSettingsView : System.Windows.Controls.UserControl
    {
        private bool _isInitialized = false;

        public CalendarSettingsView()
        {
            InitializeComponent();
            RefreshSettings();
            _isInitialized = true;

            // Subscribe to sync status updates from the background service
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService != null)
            {
                app.SyncService.SyncStatusChanged += OnSyncStatusChanged;
            }
        }

        private void OnSyncStatusChanged(string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncStatusText.Text = status;

                // Color the dot based on status
                if (status.Contains("✓") || status.Contains("Synced") || status.Contains("events"))
                {
                    SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // green
                }
                else if (status.Contains("failed") || status.Contains("error") || status.Contains("Error"))
                {
                    SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // red
                }
                else
                {
                    SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255)); // grey
                }

                // Update cached events count
                var cache = WidgetConfig.Current.CalendarEventsCache;
                if (!string.IsNullOrEmpty(cache) && cache != "[]")
                {
                    try
                    {
                        var events = System.Text.Json.JsonSerializer.Deserialize<List<CalendarEvent>>(cache);
                        int count = events?.Count ?? 0;
                        EventsCacheInfo.Text = count == 0
                            ? "No events today"
                            : $"{count} event{(count == 1 ? "" : "s")} cached for today";
                    }
                    catch
                    {
                        EventsCacheInfo.Text = "Cache unavailable";
                    }
                }
                else
                {
                    EventsCacheInfo.Text = "No events cached";
                }
            }));
        }

        private void SyncNowButton_Click(object sender, RoutedEventArgs e)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            SyncNowButton.IsEnabled = false;
            SyncNowButton.Content = "Syncing...";
            SyncStatusText.Text = "Syncing...";

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await app.SyncService.SyncNowAsync();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    SyncNowButton.IsEnabled = true;
                    SyncNowButton.Content = "Sync Now";
                }));
            });
        }

        private void CalendarEnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isEnabled = CalendarEnabledToggle.IsChecked ?? false;
            WidgetConfig.Current.CalendarEnabled = isEnabled;
            WidgetConfig.Save();

            var app = System.Windows.Application.Current as App;
            app?.SetCalendarWidgetVisibility(isEnabled);
        }

        private void CalendarLockToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isLocked = CalendarLockToggle.IsChecked ?? false;
            WidgetConfig.Current.CalendarLocked = isLocked;
            WidgetConfig.Save();

            var app = System.Windows.Application.Current as App;
            app?.ApplyCalendarLockState();
        }

        private void ConnectAccountButton_Click(object sender, RoutedEventArgs e)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            ConnectAccountButton.IsEnabled = false;
            ConnectAccountButton.Content = "Connecting in browser...";

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                bool success = await app.SyncService.ConnectGoogleCalendarAccountAsync();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    ConnectAccountButton.IsEnabled = true;
                    ConnectAccountButton.Content = "Connect Google Calendar Account";
                    RefreshConnectedAccounts();
                }));
            });
        }

        private void DisconnectAccount_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            string email = btn?.Tag as string ?? "";
            if (string.IsNullOrEmpty(email)) return;

            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to disconnect the Google Calendar account {email}?\nEvents from this account will no longer appear on your desktop.",
                "Disconnect Account",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                bool success = await app.SyncService.DisconnectGoogleCalendarAccountAsync(email);
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshConnectedAccounts();
                }));
            });
        }

        private void RefreshConnectedAccounts()
        {
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var accounts = await app.SyncService.FetchGoogleAccountsListAsync();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    AccountsList.ItemsSource = accounts;
                    NoAccountsText.Visibility = (accounts.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                }));
            });
        }

        /// <summary>
        /// Refreshes all UI controls from the current config state.
        /// </summary>
        public void RefreshSettings()
        {
            _isInitialized = false;

            CalendarEnabledToggle.IsChecked = WidgetConfig.Current.CalendarEnabled;
            CalendarLockToggle.IsChecked = WidgetConfig.Current.CalendarLocked;
            
            bool isSignedIn = !string.IsNullOrEmpty(WidgetConfig.Current.CalendarUserId);
            SyncNowButton.IsEnabled = isSignedIn;
            ConnectAccountButton.IsEnabled = isSignedIn;

            if (isSignedIn)
            {
                RefreshConnectedAccounts();

                // Initialize status labels from cache
                var cache = WidgetConfig.Current.CalendarEventsCache;
                if (!string.IsNullOrEmpty(cache) && cache != "[]")
                {
                    try
                    {
                        var events = System.Text.Json.JsonSerializer.Deserialize<List<CalendarEvent>>(cache);
                        int count = events?.Count ?? 0;
                        EventsCacheInfo.Text = count == 0
                            ? "No events today"
                            : $"{count} event{(count == 1 ? "" : "s")} cached for today";
                        SyncStatusText.Text = "Synced from cache";
                        SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // green
                    }
                    catch
                    {
                        EventsCacheInfo.Text = "Cache unavailable";
                        SyncStatusText.Text = "Not synced yet";
                        SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // red
                    }
                }
                else
                {
                    EventsCacheInfo.Text = "No events cached";
                    SyncStatusText.Text = "Not synced yet";
                    SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)); // gray
                }
            }
            else
            {
                AccountsList.ItemsSource = null;
                NoAccountsText.Visibility = Visibility.Visible;
                SyncStatusText.Text = "Not signed in";
                EventsCacheInfo.Text = "Please connect an account";
                SyncStatusDot.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)); // gray
            }

            _isInitialized = true;
        }

        private void ScrollViewer_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.15));
                e.Handled = true;
            }
        }
    }
}
