using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfWidgets
{
    public partial class ClockWidgetWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private bool _isDragging = false;
        
        // Fields for custom mouse dragging and snapping
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

        public ClockWidgetWindow()
        {
            InitializeComponent();

            // Set up timer to update clock display every 500ms
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            // Register events
            SourceInitialized += ClockWidgetWindow_SourceInitialized;
            StateChanged += ClockWidgetWindow_StateChanged;
            Activated += ClockWidgetWindow_Activated;
            _timer.Tick += Timer_Tick;
            if (WidgetConfig.Current.ClockEnabled)
            {
                _timer.Start();
            }

            // Load initial time display immediately
            UpdateClock();
        }

        public void StartClock()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                UpdateClock();
            }
        }

        public void StopClock()
        {
            _timer.Stop();
        }

        private void ClockWidgetWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Load custom size if configured
            Width = WidgetConfig.Current.ClockWidth;
            Height = WidgetConfig.Current.ClockHeight;

            // 1. Load window position from configuration
            double x = WidgetConfig.Current.ClockPositionX;
            double y = WidgetConfig.Current.ClockPositionY;

            if (x < 0 || y < 0)
            {
                // Default coordinates: Position in the center-right region of primary display
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = screenWidth - Width - 50; // 50px from right margin
                Top = 150; // 150px from top margin
            }
            else
            {
                Left = x;
                Top = y;
            }

            // 2. Attach to desktop (handles owner, Alt-Tab styling, and initial Z-order push)
            DesktopWindowHelper.AttachToDesktop(this);

            // Enable native frosted glass blur
            DesktopWindowHelper.EnableBlur(this);
            DesktopWindowHelper.SetRoundedWindowRegion(this, 12);

            // 3. Set click-through state (lock position)
            ApplyLockState();

            // 4. Setup resize hook for borderless window resizing
            SetupResizeHook();

            // 5. Initial layout sync for multiple world clocks
            RefreshClocksLayout();
        }

        private void ClockWidgetWindow_StateChanged(object? sender, EventArgs e)
        {
            // Anti-Minimize: If the OS minimizes this window (e.g. Win+D or Show Desktop swipe),
            // restore it to Normal immediately and push it back to the bottom of the Z-order.
            if (WindowState == WindowState.Minimized)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    WindowState = WindowState.Normal;
                    DesktopWindowHelper.PushToBottom(this);
                }));
            }
        }

        private void ClockWidgetWindow_Activated(object? sender, EventArgs e)
        {
            // Keep the window at the bottom of the Z-order even when focused or clicked
            DesktopWindowHelper.PushToBottom(this);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            DesktopWindowHelper.PushToBottom(this);
        }

        /// <summary>
        /// Updates the click-through state (input transparency) depending on the configuration.
        /// </summary>
        public void ApplyLockState()
        {
            DesktopWindowHelper.SetClickThrough(this, WidgetConfig.Current.ClockLocked);
            DesktopWindowHelper.PushToBottom(this);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateClock();
            // Periodically ensure the widget stays at the bottom of the Z-order
            DesktopWindowHelper.PushToBottom(this);
        }

        public void RefreshClocksLayout()
        {
            bool showBottom = WidgetConfig.Current.Clock2Enabled || WidgetConfig.Current.Clock3Enabled;

            // Toggle visibilities of secondary components
            BottomSeparator.Visibility = showBottom ? Visibility.Visible : Visibility.Collapsed;
            BottomClocksGrid.Visibility = showBottom ? Visibility.Visible : Visibility.Collapsed;

            // Toggle individual clock panel visibilities inside the grid
            Clock2Panel.Visibility = WidgetConfig.Current.Clock2Enabled ? Visibility.Visible : Visibility.Collapsed;
            Clock3Panel.Visibility = WidgetConfig.Current.Clock3Enabled ? Visibility.Visible : Visibility.Collapsed;

            // Determine active secondary clock count
            int activeSecondaryClocks = 0;
            if (WidgetConfig.Current.Clock2Enabled) activeSecondaryClocks++;
            if (WidgetConfig.Current.Clock3Enabled) activeSecondaryClocks++;

            if (activeSecondaryClocks == 1)
            {
                // Single secondary clock: display in one line, larger font
                Clock2Panel.Orientation = System.Windows.Controls.Orientation.Horizontal;
                Clock2Label.FontSize = 13.5;
                Clock2Time.FontSize = 13.5;
                Clock2Time.Margin = new Thickness(6, 0, 0, 0);

                Clock3Panel.Orientation = System.Windows.Controls.Orientation.Horizontal;
                Clock3Label.FontSize = 13.5;
                Clock3Time.FontSize = 13.5;
                Clock3Time.Margin = new Thickness(6, 0, 0, 0);
            }
            else
            {
                // Multiple secondary clocks: stacked layout
                Clock2Panel.Orientation = System.Windows.Controls.Orientation.Vertical;
                Clock2Label.FontSize = 11;
                Clock2Time.FontSize = 15;
                Clock2Time.Margin = new Thickness(0, 1, 0, 0);

                Clock3Panel.Orientation = System.Windows.Controls.Orientation.Vertical;
                Clock3Label.FontSize = 11;
                Clock3Time.FontSize = 15;
                Clock3Time.Margin = new Thickness(0, 1, 0, 0);
            }

            // Toggle primary clock custom label visibility
            bool hasCustomPrimaryLabel = !string.IsNullOrWhiteSpace(WidgetConfig.Current.Clock1Label) && 
                                          WidgetConfig.Current.Clock1Label != "Local Time";
            Clock1Label.Visibility = hasCustomPrimaryLabel ? Visibility.Visible : Visibility.Collapsed;

            // Dynamic height adjustments
            double targetHeight = showBottom ? 210 : 145;
            double targetWidth = 340;

            if (Math.Abs(Height - targetHeight) > 1 || Math.Abs(Width - targetWidth) > 1)
            {
                Height = targetHeight;
                Width = targetWidth;

                WidgetConfig.Current.ClockWidth = Width;
                WidgetConfig.Current.ClockHeight = Height;
                WidgetConfig.Save();
            }

            UpdateClock();
        }

        private string GetOffsetString(TimeZoneInfo targetTz, TimeZoneInfo primaryTz, DateTime utcNow)
        {
            try
            {
                var primaryTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, primaryTz);
                var targetTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, targetTz);
                
                var offset = targetTime - primaryTime;
                double hours = offset.TotalHours;
                
                if (hours == 0) return string.Empty;
                
                string sign = hours > 0 ? "+" : "";
                if (hours % 1 == 0)
                {
                    return $" ({sign}{(int)hours}h)";
                }
                else
                {
                    return $" ({sign}{hours:0.#}h)";
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private void UpdateClock()
        {
            DateTime utcNow = DateTime.UtcNow;
            string format = WidgetConfig.Current.Is24HourFormat ? "HH:mm:ss" : "hh:mm:ss tt";
            string secondaryFormat = WidgetConfig.Current.Is24HourFormat ? "HH:mm" : "h:mm tt";

            // Resolve Primary Timezone
            TimeZoneInfo primaryTz;
            try
            {
                primaryTz = WidgetConfig.Current.Clock1TimeZoneId == "Local"
                    ? TimeZoneInfo.Local
                    : TimeZoneInfo.FindSystemTimeZoneById(WidgetConfig.Current.Clock1TimeZoneId);
            }
            catch
            {
                primaryTz = TimeZoneInfo.Local;
            }

            // Clock 1 (Primary Clock)
            DateTime primaryTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, primaryTz);
            Clock1Label.Text = string.IsNullOrWhiteSpace(WidgetConfig.Current.Clock1Label) ? "Local Time" : WidgetConfig.Current.Clock1Label;
            Clock1Time.Text = primaryTime.ToString(format);
            
            // Format primary date without year to keep it compact (e.g. "Monday, July 6")
            Clock1Date.Text = primaryTime.ToString("dddd, MMMM d");

            // Clock 2 (Custom Timezone)
            if (WidgetConfig.Current.Clock2Enabled)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(WidgetConfig.Current.Clock2TimeZoneId);
                    DateTime tzTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                    string offsetStr = GetOffsetString(tz, primaryTz, utcNow);
                    
                    string label = string.IsNullOrWhiteSpace(WidgetConfig.Current.Clock2Label) ? "London" : WidgetConfig.Current.Clock2Label;
                    Clock2Label.Text = $"{label}{offsetStr}";
                    Clock2Time.Text = tzTime.ToString(secondaryFormat);
                }
                catch
                {
                    Clock2Label.Text = "Error TZ";
                    Clock2Time.Text = "--:--";
                }
            }

            // Clock 3 (Custom Timezone)
            if (WidgetConfig.Current.Clock3Enabled)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(WidgetConfig.Current.Clock3TimeZoneId);
                    DateTime tzTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                    string offsetStr = GetOffsetString(tz, primaryTz, utcNow);
                    
                    string label = string.IsNullOrWhiteSpace(WidgetConfig.Current.Clock3Label) ? "Tokyo" : WidgetConfig.Current.Clock3Label;
                    Clock3Label.Text = $"{label}{offsetStr}";
                    Clock3Time.Text = tzTime.ToString(secondaryFormat);
                }
                catch
                {
                    Clock3Label.Text = "Error TZ";
                    Clock3Time.Text = "--:--";
                }
            }
        }

        // Dragging & Position Saving with Grid/Edge Snapping
        private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetConfig.Current.ClockLocked) return;

            POINT mousePos;
            if (GetCursorPos(out mousePos))
            {
                _isDragging = true;
                _dragStartMouseX = mousePos.X;
                _dragStartMouseY = mousePos.Y;
                _dragStartWindowX = Left;
                _dragStartWindowY = Top;

                // Capture mouse to ensure we receive mouse events even if cursor moves fast off-widget
                var element = sender as UIElement;
                element?.CaptureMouse();
                
                LogHelper.Log($"[Drag] Drag started. Mouse start: ({_dragStartMouseX}, {_dragStartMouseY}). Window start: ({_dragStartWindowX}, {_dragStartWindowY})");
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

                // Adjust for Windows DPI Scaling (converts physical pixels to WPF DIPs)
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

                // 1. Grid Snapping: Snap to a virtual 20px grid
                const int gridSize = 20;
                double snappedLeft = Math.Round(rawLeft / gridSize) * gridSize;
                double snappedTop = Math.Round(rawTop / gridSize) * gridSize;

                // 2. Edge Snapping: Snap to screen boundaries if within 15px
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                if (Math.Abs(snappedLeft) < 15)
                {
                    snappedLeft = 0; // Snap to Left edge
                }
                else if (Math.Abs(snappedLeft + Width - screenWidth) < 15)
                {
                    snappedLeft = screenWidth - Width; // Snap to Right edge
                }

                if (Math.Abs(snappedTop) < 15)
                {
                    snappedTop = 0; // Snap to Top edge
                }
                else if (Math.Abs(snappedTop + Height - screenHeight) < 15)
                {
                    snappedTop = screenHeight - Height; // Snap to Bottom edge
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

            // Save the new snapped position
            WidgetConfig.Current.ClockPositionX = Left;
            WidgetConfig.Current.ClockPositionY = Top;
            WidgetConfig.Save();

            LogHelper.Log($"[Drag] Drag ended. Saved snapped position: ({Left}, {Top})");

            // Push back to bottom of Z-order
            DesktopWindowHelper.PushToBottom(this);
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
                System.Diagnostics.Debug.WriteLine($"[ClockWidgetWindow] Failed to update taskbar icon: {ex.Message}");
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DesktopWindowHelper.SetRoundedWindowRegion(this, 12);
            if (this.IsLoaded)
            {
                WidgetConfig.Current.ClockWidth = e.NewSize.Width;
                WidgetConfig.Current.ClockHeight = e.NewSize.Height;
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
                if (WidgetConfig.Current.ClockLocked) return IntPtr.Zero;

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
