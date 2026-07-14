using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfWidgets
{
    public partial class CalendarWidgetWindow : Window
    {
        private bool _isDragging = false;
        private int _dragStartMouseX;
        private int _dragStartMouseY;
        private double _dragStartWindowX;
        private double _dragStartWindowY;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public CalendarWidgetWindow()
        {
            InitializeComponent();

            SourceInitialized += CalendarWidgetWindow_SourceInitialized;
            StateChanged += CalendarWidgetWindow_StateChanged;
            Activated += CalendarWidgetWindow_Activated;

            UpdateDateTitle();
            LoadCachedEvents();
        }

        private void CalendarWidgetWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Load custom size if configured
            Width = WidgetConfig.Current.CalendarWidth;
            Height = WidgetConfig.Current.CalendarHeight;

            // 1. Load position
            double x = WidgetConfig.Current.CalendarPositionX;
            double y = WidgetConfig.Current.CalendarPositionY;

            if (x < 0 || y < 0)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = screenWidth - Width - 50; // 50px from right margin
                Top = 350; // Default directly below the digital clock widget
            }
            else
            {
                Left = x;
                Top = y;
            }

            // 2. Attach to background wallpaper
            DesktopWindowHelper.AttachToDesktop(this);

            // Enable native frosted glass blur
            DesktopWindowHelper.EnableBlur(this);
            DesktopWindowHelper.SetRoundedWindowRegion(this, 8);

            // 3. Set click-through state
            ApplyLockState();

            // 4. Setup resize hook for borderless window resizing
            SetupResizeHook();
        }

        private void CalendarWidgetWindow_StateChanged(object? sender, EventArgs e)
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

        private void CalendarWidgetWindow_Activated(object? sender, EventArgs e)
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
            DesktopWindowHelper.SetClickThrough(this, WidgetConfig.Current.CalendarLocked);
            DesktopWindowHelper.PushToBottom(this);
        }

        private void UpdateDateTitle()
        {
            DateText.Text = DateTime.Now.ToString("dddd, MMMM d");
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
                System.Diagnostics.Debug.WriteLine($"[CalendarWidget] Error loading cached events: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[CalendarWidget] Error updating events: {ex.Message}");
            }
        }

        public void UpdateEventsList(List<CalendarEvent> events)
        {
            UpdateDateTitle();
            
            var todayEvents = new List<CalendarEvent>();
            var tomorrowEvents = new List<CalendarEvent>();
            
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            string tomorrowStr = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
            
            if (events != null)
            {
                foreach (var ev in events)
                {
                    if (ev.StartDate == todayStr)
                    {
                        todayEvents.Add(ev);
                    }
                    else if (ev.StartDate == tomorrowStr)
                    {
                        tomorrowEvents.Add(ev);
                    }
                }
            }
            
            // Set Today's Items
            if (todayEvents.Count == 0)
            {
                TodayEmptyLabel.Visibility = Visibility.Visible;
                TodayEventsList.ItemsSource = null;
            }
            else
            {
                TodayEmptyLabel.Visibility = Visibility.Collapsed;
                TodayEventsList.ItemsSource = todayEvents;
            }
            
            // Set Tomorrow's Items
            if (tomorrowEvents.Count == 0)
            {
                TomorrowEmptyLabel.Visibility = Visibility.Visible;
                TomorrowEventsList.ItemsSource = null;
            }
            else
            {
                TomorrowEmptyLabel.Visibility = Visibility.Collapsed;
                TomorrowEventsList.ItemsSource = tomorrowEvents;
            }

            // Global empty state
            if (todayEvents.Count == 0 && tomorrowEvents.Count == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                EventsScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                EventsScrollViewer.Visibility = Visibility.Visible;
            }
            
            DesktopWindowHelper.PushToBottom(this);
        }

        // Snap and Drag
        private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetConfig.Current.CalendarLocked) return;

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

                // 1. Snapping to a 20px grid
                const int gridSize = 20;
                double snappedLeft = Math.Round(rawLeft / gridSize) * gridSize;
                double snappedTop = Math.Round(rawTop / gridSize) * gridSize;

                // 2. Snapping to screen edges (within 15px margin)
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

            // Save new positions
            WidgetConfig.Current.CalendarPositionX = Left;
            WidgetConfig.Current.CalendarPositionY = Top;
            WidgetConfig.Save();

            LogHelper.Log($"[Drag] Calendar Widget drag ended at ({Left}, {Top})");

            DesktopWindowHelper.PushToBottom(this);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService == null) return;

            RefreshButton.IsEnabled = false;
            RefreshButton.Opacity = 0.5;

            // Start Rotation Animation
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
                    // Stop Animation and reset
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
            if (this.IsLoaded)
            {
                WidgetConfig.Current.CalendarWidth = e.NewSize.Width;
                WidgetConfig.Current.CalendarHeight = e.NewSize.Height;
                WidgetConfig.Save();
            }
        }

        private const int WM_NCHITTEST = 0x0084;
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
            if (msg == WM_NCHITTEST)
            {
                if (WidgetConfig.Current.CalendarLocked) return IntPtr.Zero;

                // Get mouse coordinates relative to screen
                int x = (int)(lParam.ToInt32() & 0xFFFF);
                int y = (int)((lParam.ToInt32() >> 16) & 0xFFFF);

                // Convert to local window coordinates
                System.Windows.Point localPoint = this.PointFromScreen(new System.Windows.Point(x, y));

                double width = this.ActualWidth;
                double height = this.ActualHeight;
                double borderWidth = 16; // thickness of the hit-test border (generous for easier grabbing)

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
