using System;
using System.Windows;
using System.Windows.Controls;

namespace WpfWidgets.Views
{
    public partial class ClockSettingsView : System.Windows.Controls.UserControl
    {
        private bool _isInitialized = false;

        public ClockSettingsView()
        {
            InitializeComponent();

            // Populate Timezone dropdowns
            var timeZones = TimeZoneInfo.GetSystemTimeZones();
            
            var clock1TimeZones = new System.Collections.Generic.List<TimeZoneItem>();
            clock1TimeZones.Add(new TimeZoneItem { Id = "Local", DisplayName = "Local System Time" });
            foreach (var tz in timeZones)
            {
                clock1TimeZones.Add(new TimeZoneItem { Id = tz.Id, DisplayName = tz.DisplayName });
            }

            Clock1TimeZoneCombo.ItemsSource = clock1TimeZones;
            Clock1TimeZoneCombo.DisplayMemberPath = "DisplayName";
            Clock1TimeZoneCombo.SelectedValuePath = "Id";

            Clock2TimeZoneCombo.ItemsSource = timeZones;
            Clock2TimeZoneCombo.DisplayMemberPath = "DisplayName";
            Clock2TimeZoneCombo.SelectedValuePath = "Id";

            Clock3TimeZoneCombo.ItemsSource = timeZones;
            Clock3TimeZoneCombo.DisplayMemberPath = "DisplayName";
            Clock3TimeZoneCombo.SelectedValuePath = "Id";

            // Load values from configuration
            LoadConfiguration();

            _isInitialized = true;
        }

        private void LoadConfiguration()
        {
            TimeFormatToggle.IsChecked = WidgetConfig.Current.Is24HourFormat;
            LockPositionToggle.IsChecked = WidgetConfig.Current.ClockLocked;
            ClockEnabledToggle.IsChecked = WidgetConfig.Current.ClockEnabled;

            Clock1LabelText.Text = WidgetConfig.Current.Clock1Label;
            Clock1TimeZoneCombo.SelectedValue = WidgetConfig.Current.Clock1TimeZoneId;

            Clock2EnabledToggle.IsChecked = WidgetConfig.Current.Clock2Enabled;
            Clock2LabelText.Text = WidgetConfig.Current.Clock2Label;
            Clock2TimeZoneCombo.SelectedValue = WidgetConfig.Current.Clock2TimeZoneId;

            Clock3EnabledToggle.IsChecked = WidgetConfig.Current.Clock3Enabled;
            Clock3LabelText.Text = WidgetConfig.Current.Clock3Label;
            Clock3TimeZoneCombo.SelectedValue = WidgetConfig.Current.Clock3TimeZoneId;

            UpdateDetailsPanelsVisibility();
        }

        private void UpdateDetailsPanelsVisibility()
        {
            Clock2DetailsPanel.Visibility = (Clock2EnabledToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            Clock3DetailsPanel.Visibility = (Clock3EnabledToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TimeFormatToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            WidgetConfig.Current.Is24HourFormat = TimeFormatToggle.IsChecked ?? false;
            WidgetConfig.Save();
            NotifyWidgetRefresh();
        }

        private void LockPositionToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isLocked = LockPositionToggle.IsChecked ?? false;
            WidgetConfig.Current.ClockLocked = isLocked;
            WidgetConfig.Save();

            // Sync with main clock window
            var clockWindow = System.Windows.Application.Current.MainWindow as ClockWidgetWindow;
            clockWindow?.ApplyLockState();
        }

        private void ClockEnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isEnabled = ClockEnabledToggle.IsChecked ?? false;
            WidgetConfig.Current.ClockEnabled = isEnabled;
            WidgetConfig.Save();

            // Notify App of visibility change
            var app = System.Windows.Application.Current as App;
            app?.SetClockWidgetVisibility(isEnabled);
        }

        private void Clock2EnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            WidgetConfig.Current.Clock2Enabled = Clock2EnabledToggle.IsChecked ?? false;
            WidgetConfig.Save();
            UpdateDetailsPanelsVisibility();
            NotifyWidgetRefresh();
        }

        private void Clock3EnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            WidgetConfig.Current.Clock3Enabled = Clock3EnabledToggle.IsChecked ?? false;
            WidgetConfig.Save();
            UpdateDetailsPanelsVisibility();
            NotifyWidgetRefresh();
        }

        private void ClockSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            WidgetConfig.Current.Clock1Label = Clock1LabelText.Text;
            if (Clock1TimeZoneCombo.SelectedValue is string tz1)
            {
                WidgetConfig.Current.Clock1TimeZoneId = tz1;
            }
            WidgetConfig.Current.Clock2Label = Clock2LabelText.Text;
            if (Clock2TimeZoneCombo.SelectedValue is string tz2)
            {
                WidgetConfig.Current.Clock2TimeZoneId = tz2;
            }
            WidgetConfig.Current.Clock3Label = Clock3LabelText.Text;
            if (Clock3TimeZoneCombo.SelectedValue is string tz3)
            {
                WidgetConfig.Current.Clock3TimeZoneId = tz3;
            }

            WidgetConfig.Save();
            NotifyWidgetRefresh();
        }

        private void NotifyWidgetRefresh()
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is ClockWidgetWindow clockWin)
                {
                    clockWin.RefreshClocksLayout();
                    break;
                }
            }
        }

        /// <summary>
        /// Refreshes settings UI controls from the active configuration, temporarily disabling trigger events.
        /// </summary>
        public void RefreshSettings()
        {
            _isInitialized = false;
            LoadConfiguration();
            _isInitialized = true;
        }
    }

    public class TimeZoneItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
