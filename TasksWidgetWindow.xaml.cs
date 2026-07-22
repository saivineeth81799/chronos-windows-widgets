using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfWidgets
{
    public partial class TasksWidgetWindow : Window
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

        public TasksWidgetWindow()
        {
            InitializeComponent();

            SourceInitialized += TasksWidgetWindow_SourceInitialized;
            StateChanged += TasksWidgetWindow_StateChanged;
            Activated += TasksWidgetWindow_Activated;

            LoadCachedTasks();
        }

        private void TasksWidgetWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Load custom size if configured
            Width = WidgetConfig.Current.TasksWidth;
            Height = WidgetConfig.Current.TasksHeight;

            // 1. Load position
            double x = WidgetConfig.Current.TasksPositionX;
            double y = WidgetConfig.Current.TasksPositionY;

            if (x < 0 || y < 0)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                Left = screenWidth - Width - 50; // 50px from right margin
                Top = 550; // Below calendar widget
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

        private void TasksWidgetWindow_StateChanged(object? sender, EventArgs e)
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

        private void TasksWidgetWindow_Activated(object? sender, EventArgs e)
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
            DesktopWindowHelper.SetClickThrough(this, WidgetConfig.Current.TasksLocked);
            DesktopWindowHelper.PushToBottom(this);
        }

        private void LoadCachedTasks()
        {
            try
            {
                string cache = WidgetConfig.Current.TasksCache;
                if (!string.IsNullOrEmpty(cache) && cache != "[]")
                {
                    var items = JsonSerializer.Deserialize<List<WidgetTaskItem>>(cache);
                    if (items != null)
                    {
                        LogHelper.Log($"[TasksWidget] Loaded {items.Count} cached task item(s).");
                        UpdateTasksList(items);
                    }
                }
                else
                {
                    LogHelper.Log("[TasksWidget] Cached tasks cache is empty or null.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[TasksWidget] Error loading cached tasks: {ex.Message}");
            }
        }

        public void UpdateTasksList(List<WidgetTaskItem> tasks)
        {
            int count = tasks?.Count ?? 0;
            LogHelper.Log($"[TasksWidget] UpdateTasksList called with {count} task item(s).");

            if (tasks == null || tasks.Count == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                TasksScrollViewer.Visibility = Visibility.Collapsed;
                TasksItemsControl.ItemsSource = null;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                TasksScrollViewer.Visibility = Visibility.Visible;
                TasksItemsControl.ItemsSource = tasks;
            }

            DesktopWindowHelper.PushToBottom(this);
        }

        // Snap and Drag
        private void WidgetCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WidgetConfig.Current.TasksLocked) return;

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

                // Snapping to a 20px grid
                const int gridSize = 20;
                double snappedLeft = Math.Round(rawLeft / gridSize) * gridSize;
                double snappedTop = Math.Round(rawTop / gridSize) * gridSize;

                // Snapping to screen edges (within 15px margin)
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                const double snapMargin = 15.0;

                if (Math.Abs(snappedLeft) < snapMargin) snappedLeft = 0;
                if (Math.Abs(snappedLeft + Width - screenWidth) < snapMargin) snappedLeft = screenWidth - Width;
                if (Math.Abs(snappedTop) < snapMargin) snappedTop = 0;
                if (Math.Abs(snappedTop + Height - screenHeight) < snapMargin) snappedTop = screenHeight - Height;

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

            // Save new coordinates to configuration
            WidgetConfig.Current.TasksPositionX = Left;
            WidgetConfig.Current.TasksPositionY = Top;
            WidgetConfig.Save();

            LogHelper.Log($"[TasksWidget] Position saved: ({Left}, {Top})");
            DesktopWindowHelper.PushToBottom(this);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.Log("[TasksWidget] Refresh button clicked by user. Initiating Firebase sync...");
            var app = System.Windows.Application.Current as App;
            if (app?.SyncService != null)
            {
                // Play rotate animation
                var rotation = RefreshButtonRotation;
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                rotation.BeginAnimation(RotateTransform.AngleProperty, anim);

                await app.SyncService.SyncNowAsync();
            }
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
                WidgetConfig.Current.TasksWidth = this.ActualWidth;
                WidgetConfig.Current.TasksHeight = this.ActualHeight;
                WidgetConfig.Current.TasksPositionX = this.Left;
                WidgetConfig.Current.TasksPositionY = this.Top;
                WidgetConfig.Save();
                LogHelper.Log($"[Size/Move] Saved Tasks Widget layout: size={ActualWidth}x{ActualHeight}, pos={Left},{Top}");
            }
            else if (msg == WM_NCHITTEST)
            {
                if (WidgetConfig.Current.TasksLocked) return IntPtr.Zero;

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
