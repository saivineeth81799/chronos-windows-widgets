using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace WpfWidgets.Views
{
    public partial class TasksSettingsView : System.Windows.Controls.UserControl
    {
        private bool _isInitialized = false;

        public TasksSettingsView()
        {
            InitializeComponent();

            // Load values from configuration
            TasksEnabledToggle.IsChecked = WidgetConfig.Current.TasksEnabled;
            LockPositionToggle.IsChecked = WidgetConfig.Current.TasksLocked;

            PopulateViewsDropdown();

            _isInitialized = true;
        }

        private void PopulateViewsDropdown()
        {
            var options = new List<ViewComboItem>
            {
                new ViewComboItem { DisplayName = "Follow Mobile App", Id = "FollowMobile" },
                new ViewComboItem { DisplayName = "Plain View (All Tasks)", Id = "Plain" }
            };

            try
            {
                string cachedViewsJson = WidgetConfig.Current.TasksCustomViewsCache;
                if (!string.IsNullOrEmpty(cachedViewsJson) && cachedViewsJson != "[]")
                {
                    var views = JsonSerializer.Deserialize<List<CustomViewItem>>(cachedViewsJson);
                    if (views != null)
                    {
                        foreach (var view in views)
                        {
                            options.Add(new ViewComboItem { DisplayName = view.Name, Id = view.Id });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TasksSettings] Error parsing custom views cache: {ex.Message}");
            }

            ViewSelector.ItemsSource = options;
            ViewSelector.DisplayMemberPath = "DisplayName";
            ViewSelector.SelectedValuePath = "Id";

            // Set selected value
            string selectedId = WidgetConfig.Current.TasksSelectedViewId ?? "FollowMobile";
            ViewSelector.SelectedValue = selectedId;
            if (ViewSelector.SelectedIndex == -1)
            {
                ViewSelector.SelectedIndex = 0; // Fallback to Follow Mobile
            }
        }

        private void TasksEnabledToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isEnabled = TasksEnabledToggle.IsChecked ?? false;
            WidgetConfig.Current.TasksEnabled = isEnabled;
            WidgetConfig.Save();

            // Notify App of visibility change
            var app = System.Windows.Application.Current as App;
            app?.SetTasksWidgetVisibility(isEnabled);
        }

        private void LockPositionToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isLocked = LockPositionToggle.IsChecked ?? false;
            WidgetConfig.Current.TasksLocked = isLocked;
            WidgetConfig.Save();

            // Sync with main tasks window
            var app = System.Windows.Application.Current as App;
            app?.ApplyTasksLockState();
        }

        private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            string? selectedId = ViewSelector.SelectedValue as string;
            if (string.IsNullOrEmpty(selectedId)) return;

            WidgetConfig.Current.TasksSelectedViewId = selectedId;
            WidgetConfig.Save();

            // Trigger sync instantly to apply the new view to the widget
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService != null)
            {
                _ = app.SyncService.SyncNowAsync();
            }
        }

        /// <summary>
        /// Refreshes settings UI controls from the active configuration, temporarily disabling trigger events.
        /// </summary>
        public void RefreshSettings()
        {
            _isInitialized = false;
            TasksEnabledToggle.IsChecked = WidgetConfig.Current.TasksEnabled;
            LockPositionToggle.IsChecked = WidgetConfig.Current.TasksLocked;
            PopulateViewsDropdown();
            _isInitialized = true;
        }

        private class ViewComboItem
        {
            public string DisplayName { get; set; } = "";
            public string Id { get; set; } = "";
        }
    }
}
