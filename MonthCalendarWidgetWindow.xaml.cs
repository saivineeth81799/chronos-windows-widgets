using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfWidgets
{
    public partial class MonthCalendarWidgetWindow : Window
    {
        private bool _isDragging = false;
        private int _dragStartMouseX;
        private int _dragStartMouseY;
        private double _dragStartWindowX;
        private double _dragStartWindowY;
        private List<CalendarEvent> _allEvents = new();

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public MonthCalendarWidgetWindow()
        {
            InitializeComponent();

            SourceInitialized += MonthCalendarWidgetWindow_SourceInitialized;
            StateChanged += MonthCalendarWidgetWindow_StateChanged;
            Activated += MonthCalendarWidgetWindow_Activated;

            MonthCalendarControl.SelectedDate = DateTime.Today;
            LoadCachedEvents();
        }

        private void MonthCalendarWidgetWindow_SourceInitialized(object? sender, EventArgs e)
        {
            Width = WidgetConfig.Current.MonthCalendarWidth;
            Height = WidgetConfig.Current.MonthCalendarHeight;

            double x = WidgetConfig.Current.MonthCalendarPositionX;
            double y = WidgetConfig.Current.MonthCalendarPositionY;

            if (x < 0 || y < 0)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = Math.Max(50, (screenWidth - Width) / 2);
                Top = Math.Max(50, (screenHeight - Height) / 2);
            }
            else
            {
                Left = x;
                Top = y;
            }

            DesktopWindowHelper.AttachToDesktop(this);
            DesktopWindowHelper.EnableBlur(this);
            DesktopWindowHelper.SetRoundedWindowRegion(this, 8);

            ApplyLockState();
            SetupResizeHook();
        }

        private void MonthCalendarWidgetWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    WindowState = WindowState.Normal;
                    DesktopWindowHelper.PushToBottom(this);
                }));
            }
        }

        private void MonthCalendarWidgetWindow_Activated(object? sender, EventArgs e)
        {
            DesktopWindowHelper.PushToBottom(this);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            DesktopWindowHelper.PushToBottom(this);
        }

        public void ApplyLockState()
        {
            DesktopWindowHelper.SetClickThrough(this, WidgetConfig.Current.MonthCalendarLocked);
            DesktopWindowHelper.PushToBottom(this);
        }

        private void LoadCachedEvents()
        {
            try
            {
                string cache = WidgetConfig.Current.CalendarEventsCache;
                if (!string.IsNullOrEmpty(cache) && cache != "[]")
                {
                    var events = JsonSerializer.Deserialize<List<CalendarEvent>>(cache);
                    if (events != null)
                    {
                        UpdateEventsList(events);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonthCalendarWidget] Error loading cached events: {ex.Message}");
            }
        }

        public void UpdateEvents(string json)
        {
            try
            {
                var events = JsonSerializer.Deserialize<List<CalendarEvent>>(json);
                if (events != null)
                {
                    UpdateEventsList(events);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonthCalendarWidget] Error updating events: {ex.Message}");
            }
        }

        public void UpdateEventsList(List<CalendarEvent> events)
        {
            _allEvents = events ?? new List<CalendarEvent>();
            FilterAndDisplayEventsForSelectedDate();
            DesktopWindowHelper.PushToBottom(this);
        }

        private HashSet<string> _fetchedMonthKeys = new();
        private bool _isLoadingMonth = false;

        private void MonthCalendarControl_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
        {
            FilterAndDisplayEventsForSelectedDate();
        }

        private void MonthCalendarControl_DisplayDateChanged(object? sender, CalendarDateChangedEventArgs e)
        {
            DateTime newDisplayDate = MonthCalendarControl.DisplayDate;
            string monthKey = newDisplayDate.ToString("yyyy-MM");

            if (_fetchedMonthKeys.Contains(monthKey)) return;

            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            _fetchedMonthKeys.Add(monthKey);
            _isLoadingMonth = true;

            LoadingStateIndicator.Visibility = Visibility.Visible;
            EmptyStateText.Visibility = Visibility.Collapsed;
            EventsScrollViewer.Visibility = Visibility.Collapsed;

            DateTime firstDay = new DateTime(newDisplayDate.Year, newDisplayDate.Month, 1).AddDays(-7);
            DateTime lastDay = new DateTime(newDisplayDate.Year, newDisplayDate.Month, DateTime.DaysInMonth(newDisplayDate.Year, newDisplayDate.Month)).AddDays(7);

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var newEvents = await app.SyncService.FetchEventsForRangeAsync(firstDay, lastDay);
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isLoadingMonth = false;
                    LoadingStateIndicator.Visibility = Visibility.Collapsed;

                    if (newEvents != null && newEvents.Count > 0)
                    {
                        var existingIds = new HashSet<string>();
                        foreach (var ev in _allEvents)
                        {
                            existingIds.Add(ev.Id);
                        }

                        foreach (var ev in newEvents)
                        {
                            if (!existingIds.Contains(ev.Id))
                            {
                                _allEvents.Add(ev);
                            }
                        }
                    }

                    FilterAndDisplayEventsForSelectedDate();
                }));
            });
        }

        private void FilterAndDisplayEventsForSelectedDate()
        {
            try
            {
                if (_isLoadingMonth)
                {
                    LoadingStateIndicator.Visibility = Visibility.Visible;
                    EmptyStateText.Visibility = Visibility.Collapsed;
                    EventsScrollViewer.Visibility = Visibility.Collapsed;
                    return;
                }

                LoadingStateIndicator.Visibility = Visibility.Collapsed;

                DateTime selectedDate = MonthCalendarControl.SelectedDate ?? DateTime.Today;
                DateTime nextDay = selectedDate.AddDays(1);

                bool isToday = (selectedDate.Date == DateTime.Today);

                string selectedDateFormatted = selectedDate.ToString("MMM d").ToUpper();
                string nextDayFormatted = nextDay.ToString("MMM d").ToUpper();

                if (isToday)
                {
                    SelectedDateHeader.Text = $"TODAY ({selectedDateFormatted})";
                    NextDayHeader.Text = $"TOMORROW ({nextDayFormatted})";
                }
                else
                {
                    SelectedDateHeader.Text = selectedDate.ToString("dddd, MMM d").ToUpper();
                    NextDayHeader.Text = $"NEXT DAY ({nextDayFormatted})";
                }

                string selectedDateStr = selectedDate.ToString("yyyy-MM-dd");
                string nextDayStr = nextDay.ToString("yyyy-MM-dd");

                var selEvents = new List<CalendarEvent>();
                var nextEvents = new List<CalendarEvent>();

                if (_allEvents != null)
                {
                    foreach (var ev in _allEvents)
                    {
                        string evDate = !string.IsNullOrEmpty(ev.StartDate) ? ev.StartDate : ev.Date;
                        if (evDate == selectedDateStr)
                        {
                            selEvents.Add(ev);
                        }
                        else if (evDate == nextDayStr)
                        {
                            nextEvents.Add(ev);
                        }
                    }
                }

                // Selected Date Items
                if (selEvents.Count == 0)
                {
                    SelectedDateEmptyLabel.Text = isToday ? "No events today" : "No events on this date";
                    SelectedDateEmptyLabel.Visibility = Visibility.Visible;
                    SelectedDateEventsList.ItemsSource = null;
                }
                else
                {
                    SelectedDateEmptyLabel.Visibility = Visibility.Collapsed;
                    SelectedDateEventsList.ItemsSource = selEvents;
                }

                // Next Day Items
                if (nextEvents.Count == 0)
                {
                    NextDayEmptyLabel.Text = isToday ? "No events tomorrow" : "No events next day";
                    NextDayEmptyLabel.Visibility = Visibility.Visible;
                    NextDayEventsList.ItemsSource = null;
                }
                else
                {
                    NextDayEmptyLabel.Visibility = Visibility.Collapsed;
                    NextDayEventsList.ItemsSource = nextEvents;
                }

                // Global empty state
                if (selEvents.Count == 0 && nextEvents.Count == 0)
                {
                    EmptyStateText.Visibility = Visibility.Visible;
                    EventsScrollViewer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    EmptyStateText.Visibility = Visibility.Collapsed;
                    EventsScrollViewer.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonthCalendarWidget] Error in FilterAndDisplayEventsForSelectedDate: {ex.Message}");
            }
        }

        // Dragging & Snapping
        private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetConfig.Current.MonthCalendarLocked) return;

            POINT mousePos;
            if (GetCursorPos(out mousePos))
            {
                _isDragging = true;
                _dragStartMouseX = mousePos.X;
                _dragStartMouseY = mousePos.Y;
                _dragStartWindowX = Left;
                _dragStartWindowY = Top;

                var element = sender as UIElement;
                element?.CaptureMouse();
            }
        }

        private void WidgetCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;

            POINT mousePos;
            if (GetCursorPos(out mousePos))
            {
                double deltaX = mousePos.X - _dragStartMouseX;
                double deltaY = mousePos.Y - _dragStartMouseY;

                double scaleX = 1.0;
                double scaleY = 1.0;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                double deltaXDIP = deltaX / scaleX;
                double deltaYDIP = deltaY / scaleY;

                double rawLeft = _dragStartWindowX + deltaXDIP;
                double rawTop = _dragStartWindowY + deltaYDIP;

                const int gridSize = 20;
                double snappedLeft = Math.Round(rawLeft / gridSize) * gridSize;
                double snappedTop = Math.Round(rawTop / gridSize) * gridSize;

                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                if (Math.Abs(snappedLeft) < 15)
                {
                    snappedLeft = 0;
                }
                else if (Math.Abs(snappedLeft + Width - screenWidth) < 15)
                {
                    snappedLeft = screenWidth - Width;
                }

                if (Math.Abs(snappedTop) < 15)
                {
                    snappedTop = 0;
                }
                else if (Math.Abs(snappedTop + Height - screenHeight) < 15)
                {
                    snappedTop = screenHeight - Height;
                }

                Left = snappedLeft;
                Top = snappedTop;
            }
        }

        private void WidgetCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;

            var element = sender as UIElement;
            element?.ReleaseMouseCapture();

            WidgetConfig.Current.MonthCalendarPositionX = Left;
            WidgetConfig.Current.MonthCalendarPositionY = Top;
            WidgetConfig.Save();

            LogHelper.Log($"[Drag] Month Calendar Widget drag ended at ({Left}, {Top})");
            DesktopWindowHelper.PushToBottom(this);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            RefreshButton.IsEnabled = false;
            RefreshButton.Opacity = 0.5;

            var rotationAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1.2)),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            RefreshButtonRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnim);

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await app.SyncService.SyncNowAsync();
                
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshButtonRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                    RefreshButton.IsEnabled = true;
                    RefreshButton.Opacity = 0.7;
                }));
            });
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.5));
                e.Handled = true;
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DesktopWindowHelper.SetRoundedWindowRegion(this, 8);
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private void SetupResizeHook()
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_EXITSIZEMOVE)
            {
                WidgetConfig.Current.MonthCalendarWidth = this.ActualWidth;
                WidgetConfig.Current.MonthCalendarHeight = this.ActualHeight;
                WidgetConfig.Current.MonthCalendarPositionX = this.Left;
                WidgetConfig.Current.MonthCalendarPositionY = this.Top;
                WidgetConfig.Save();
                LogHelper.Log($"[Size/Move] Saved Month Calendar Widget layout: size={ActualWidth}x{ActualHeight}, pos={Left},{Top}");
            }
            else if (msg == WM_NCHITTEST)
            {
                if (WidgetConfig.Current.MonthCalendarLocked) return IntPtr.Zero;

                // Get mouse coordinates relative to screen
                int x = (int)(lParam.ToInt32() & 0xFFFF);
                int y = (int)((lParam.ToInt32() >> 16) & 0xFFFF);

                // Convert to local window coordinates
                System.Windows.Point localPoint = this.PointFromScreen(new System.Windows.Point(x, y));

                double width = this.ActualWidth;
                double height = this.ActualHeight;
                double borderWidth = 16; // thickness of the hit-test border for easy grabbing

                bool left = localPoint.X < borderWidth;
                bool right = localPoint.X > width - borderWidth;
                bool top = localPoint.Y < borderWidth;
                bool bottom = localPoint.Y > height - borderWidth;

                if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
                if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
                if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                if (left) { handled = true; return new IntPtr(HTLEFT); }
                if (right) { handled = true; return new IntPtr(HTRIGHT); }
                if (top) { handled = true; return new IntPtr(HTTOP); }
                if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }
            }
            return IntPtr.Zero;
        }
    }
}
