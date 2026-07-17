using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace WpfWidgets
{
    public static class DesktopWindowHelper
    {
        // Delegates
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        // Win32 API Imports
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return GetWindowLongPtr32(hWnd, nIndex);
        }

        private static IntPtr GetWindowOwner(IntPtr hWnd)
        {
            return GetWindowLongPtr(hWnd, GWL_HWNDPARENT);
        }

        [DllImport("user32.dll")]
        private static extern bool GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_HWNDPARENT = -8;

        // SetWindowPos Constants
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const uint GA_ROOT = 2;

        private const uint WM_SPAWN_WORKER = 0x052C;

        // Show Desktop detection constants
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int SW_RESTORE = 9;

        // Tracked widget windows and hook state
        private static readonly System.Collections.Generic.List<Window> _trackedWindows = new();
        private static IntPtr _winEventHook = IntPtr.Zero;
        private static WinEventDelegate? _winEventProc; // prevent GC collection of delegate
        private static bool _isShowingDesktop = false;
        private static IntPtr _activeWorkerW = IntPtr.Zero;
        private static System.Windows.Threading.DispatcherTimer? _restoreTimer;

        /// <summary>
        /// Finds the active WorkerW window behind the desktop icons.
        /// </summary>
        public static IntPtr GetWorkerW()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;

            // Spawn the WorkerW background window if not already present
            SendMessageTimeout(progman, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

            IntPtr workerW = IntPtr.Zero;

            // Retry up to 5 times with a 30ms delay to give the OS time to spawn the WorkerW container
            for (int retry = 0; retry < 5; retry++)
            {
                // Method A: Sibling search (standard desktop layout)
                EnumWindows((hwnd, lParam) =>
                {
                    IntPtr shell = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shell != IntPtr.Zero)
                    {
                        // The wallpaper container sits immediately behind the shell view container
                        workerW = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);

                // Method B: Child search (Windows 11 direct child layout)
                if (workerW == IntPtr.Zero)
                {
                    workerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                }

                // Method C: Fallback traversal
                if (workerW == IntPtr.Zero)
                {
                    IntPtr fallbackWorker = IntPtr.Zero;
                    EnumWindows((hwnd, lParam) =>
                    {
                        StringBuilder className = new StringBuilder(256);
                        if (GetClassName(hwnd, className, className.Capacity) && className.ToString() == "WorkerW")
                        {
                            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                            {
                                fallbackWorker = hwnd;
                                if (IsWindowVisible(hwnd))
                                {
                                    workerW = hwnd;
                                    return false; // Stop enumeration
                                }
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (workerW == IntPtr.Zero)
                    {
                        workerW = fallbackWorker;
                    }
                }

                // If found, break the retry loop
                if (workerW != IntPtr.Zero)
                {
                    break;
                }

                // Wait 30ms before retrying
                System.Threading.Thread.Sleep(30);
            }

            // Cache the active worker handle
            if (workerW != IntPtr.Zero)
            {
                _activeWorkerW = workerW;
            }

            return workerW;
        }

        /// <summary>
        /// Configures the window to stay on the desktop background layer.
        /// </summary>
        public static bool AttachToDesktop(Window window)
        {
            try
            {
                IntPtr hParent = GetWorkerW();
                LogHelper.Log($"[DesktopWindowHelper] GetWorkerW returned: {hParent.ToInt64():X}");
                if (hParent == IntPtr.Zero)
                {
                    // Fall back to Progman directly if no WorkerW is available
                    hParent = FindWindow("Progman", null);
                    LogHelper.Log($"[DesktopWindowHelper] Fallback to Progman: {hParent.ToInt64():X}");
                }

                if (hParent == IntPtr.Zero)
                {
                    LogHelper.Log("[DesktopWindowHelper] Error: No desktop window handle found.");
                    return false;
                }

                // 1. Establish the desktop wallpaper window directly as the native Owner of this WPF window.
                // This ensures our window always renders above the wallpaper but below all normal apps,
                // and prevents Windows from hiding it during Win+D.
                var helper = new WindowInteropHelper(window);
                helper.Owner = hParent;
                LogHelper.Log($"[DesktopWindowHelper] Window HWND: {helper.Handle.ToInt64():X} successfully owned by: {hParent.ToInt64():X}");

                // 2. Hide the window from the Alt+Tab menu (ToolWindow style)
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

                // 3. Force the window to the bottom of the Z-order
                PushToBottom(window);

                // 4. Track this window for Show Desktop recovery
                lock (_trackedWindows)
                {
                    if (!_trackedWindows.Contains(window))
                    {
                        _trackedWindows.Add(window);
                    }
                }

                // 5. Start the global Show Desktop watcher (once)
                StartShowDesktopWatcher();

                // 6. Start the periodic restore timer (once)
                StartRestoreTimer();

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DesktopWindowHelper] Exception during attach: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DesktopWindowHelper] Failed to attach: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Installs a global WinEvent hook to detect Show Desktop (Win+D / gesture).
        /// When the shell's WorkerW or Progman becomes the foreground window, all tracked
        /// widgets are force-restored and made temporarily topmost so they remain visible.
        /// When a normal app takes focus, widgets are pushed back to the bottom Z-order.
        /// </summary>
        private static void StartShowDesktopWatcher()
        {
            if (_winEventHook != IntPtr.Zero) return; // already installed

            // Store delegate in a field to prevent garbage collection
            _winEventProc = new WinEventDelegate(OnForegroundChanged);

            _winEventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc,
                0, 0, WINEVENT_OUTOFCONTEXT);

            if (_winEventHook != IntPtr.Zero)
            {
                LogHelper.Log("[DesktopWindowHelper] Show Desktop watcher installed.");
            }
            else
            {
                LogHelper.Log("[DesktopWindowHelper] WARNING: Failed to install Show Desktop watcher.");
            }
        }

        /// <summary>
        /// Starts the periodic timer to monitor and restore widget window state and ownership.
        /// </summary>
        private static void StartRestoreTimer()
        {
            if (_restoreTimer != null) return;

            _restoreTimer = new System.Windows.Threading.DispatcherTimer();
            _restoreTimer.Interval = TimeSpan.FromSeconds(2);
            _restoreTimer.Tick += (s, e) =>
            {
                ReattachAndRestoreWidgets();
            };
            _restoreTimer.Start();
            LogHelper.Log("[DesktopWindowHelper] Periodic widget restore timer started.");
        }

        /// <summary>
        /// Scans all tracked widgets and ensures they are correctly owned by the active WorkerW,
        /// and that they are restored if they have been minimized or hidden by the OS.
        /// </summary>
        public static void ReattachAndRestoreWidgets()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(ReattachAndRestoreWidgets));
                return;
            }

            try
            {
                IntPtr hParent = GetWorkerW();
                if (hParent == IntPtr.Zero)
                {
                    hParent = FindWindow("Progman", null);
                }

                if (hParent == IntPtr.Zero) return;

                // Track if WorkerW changed globally
                bool parentChanged = hParent != _activeWorkerW;
                if (parentChanged)
                {
                    LogHelper.Log($"[DesktopWindowHelper] ReattachAndRestoreWidgets: WorkerW handle changed from {_activeWorkerW.ToInt64():X} to {hParent.ToInt64():X}");
                    _activeWorkerW = hParent;
                }

                lock (_trackedWindows)
                {
                    foreach (var window in _trackedWindows)
                    {
                        try
                        {
                            var helper = new WindowInteropHelper(window);
                            IntPtr hWnd = helper.Handle;
                            if (hWnd == IntPtr.Zero) continue;

                            IntPtr currentOwner = GetWindowOwner(hWnd);
                            bool needsReattach = currentOwner != hParent;

                            // Check if window was hidden natively but is supposed to be visible in WPF
                            bool isNativelyVisible = IsWindowVisible(hWnd);
                            bool isWpfVisible = window.Visibility == Visibility.Visible;
                            bool needsRestore = isWpfVisible && (!isNativelyVisible || window.WindowState == WindowState.Minimized);

                            if (needsReattach || needsRestore)
                            {
                                if (needsReattach)
                                {
                                    LogHelper.Log($"[DesktopWindowHelper] Widget owner invalid (current: {currentOwner.ToInt64():X}, expected: {hParent.ToInt64():X}). Re-attaching.");
                                    helper.Owner = hParent;
                                }

                                if (isWpfVisible)
                                {
                                    if (window.WindowState == WindowState.Minimized)
                                    {
                                        LogHelper.Log($"[DesktopWindowHelper] Restoring minimized widget: {window.GetType().Name}");
                                        window.WindowState = WindowState.Normal;
                                    }

                                    // Force visibility state natively and position at bottom
                                    LogHelper.Log($"[DesktopWindowHelper] Restoring visibility and pushing to bottom for: {window.GetType().Name}");
                                    SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, 
                                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                                    
                                    PushToBottom(window);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Log($"[DesktopWindowHelper] Error restoring widget: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DesktopWindowHelper] Error in ReattachAndRestoreWidgets: {ex.Message}");
            }
        }

        /// <summary>
        /// Callback fired whenever the foreground window changes.
        /// Detects Show Desktop by checking if WorkerW or Progman became foreground.
        /// Also detects if WorkerW was recreated due to wallpaper slideshow changes.
        /// </summary>
        private static void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                // Dynamic check: Did the desktop WorkerW change (slideshow wallpaper transition)?
                IntPtr currentWorkerW = GetWorkerW();
                if (currentWorkerW != IntPtr.Zero && currentWorkerW != _activeWorkerW)
                {
                    LogHelper.Log($"[DesktopWindowHelper] WorkerW handle changed from {_activeWorkerW.ToInt64():X} to {currentWorkerW.ToInt64():X}. Triggering reattach and restore.");
                    _activeWorkerW = currentWorkerW;
                    ReattachAndRestoreWidgets();
                }

                if (hwnd == IntPtr.Zero) return;

                // Identify the class name of the new foreground window
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                string cls = className.ToString();

                bool isDesktopForeground = cls == "WorkerW" || cls == "Progman";

                if (isDesktopForeground && !_isShowingDesktop)
                {
                    _isShowingDesktop = true;
                    LogHelper.Log("[DesktopWindowHelper] Show Desktop detected — restoring widgets.");

                    // Force all tracked widgets visible and temporarily topmost
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        lock (_trackedWindows)
                        {
                            foreach (var window in _trackedWindows)
                            {
                                try
                                {
                                    if (!window.IsVisible) continue;

                                    IntPtr hWnd = new WindowInteropHelper(window).Handle;
                                    if (hWnd == IntPtr.Zero) continue;

                                    // Force restore in case the shell minimized it
                                    if (window.WindowState == WindowState.Minimized)
                                    {
                                        window.WindowState = WindowState.Normal;
                                    }

                                    // Temporarily make topmost so it renders above the desktop layer
                                    SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                                    // Immediately revert to non-topmost and push to bottom,
                                    // this ensures the widget is visible but not blocking other apps
                                    SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                                    SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0,
                                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Log($"[DesktopWindowHelper] Failed to restore widget: {ex.Message}");
                                }
                            }
                        }
                    }));
                }
                else if (!isDesktopForeground && _isShowingDesktop)
                {
                    _isShowingDesktop = false;
                    LogHelper.Log("[DesktopWindowHelper] App gained focus — pushing widgets to bottom.");

                    // Normal app took focus, ensure widgets are back at the bottom
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        lock (_trackedWindows)
                        {
                            foreach (var window in _trackedWindows)
                            {
                                try
                                {
                                    PushToBottom(window);
                                }
                                catch { }
                            }
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DesktopWindowHelper] OnForegroundChanged exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a window from tracking and cleans up the hook if no windows remain.
        /// </summary>
        public static void DetachFromDesktop(Window window)
        {
            lock (_trackedWindows)
            {
                _trackedWindows.Remove(window);
            }
        }

        /// <summary>
        /// Unhooks the global event listener. Call on app exit.
        /// </summary>
        public static void StopShowDesktopWatcher()
        {
            if (_restoreTimer != null)
            {
                _restoreTimer.Stop();
                _restoreTimer = null;
                LogHelper.Log("[DesktopWindowHelper] Periodic widget restore timer stopped.");
            }
            if (_winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
                LogHelper.Log("[DesktopWindowHelper] Show Desktop watcher unhooked.");
            }
            lock (_trackedWindows)
            {
                _trackedWindows.Clear();
            }
        }

        /// <summary>
        /// Pushes the window to the bottom of the Z-order (behind all normal windows).
        /// </summary>
        public static void PushToBottom(Window window)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;

                // Push to bottom of Z-order without moving, resizing, or activating it
                SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DesktopWindowHelper] Failed to push to bottom: {ex.Message}");
            }
        }

        /// <summary>
        /// Enables/disables mouse click-through for a WPF window.
        /// </summary>
        public static void SetClickThrough(Window window, bool clickThrough)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;

                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

                if (clickThrough)
                {
                    // Add transparent style (ignore mouse inputs)
                    SetWindowLong(hWnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    // Remove transparent style
                    SetWindowLong(hWnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DesktopWindowHelper] Failed to set click-through: {ex.Message}");
            }
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTBYPASS = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWA_BORDER_COLOR = 34;

        /// <summary>
        /// Enables frosted glass blur effect (Acrylic) behind a window based on configuration.
        /// </summary>
        public static void EnableBlur(Window window)
        {
            ApplyBlurState(window, WidgetConfig.Current.WidgetBlurEnabled);
        }

        /// <summary>
        /// Applies the frosted glass Acrylic blur or disables it dynamically.
        /// </summary>
        public static void ApplyBlurState(Window window, bool enable)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;

                // Natively round window corners (rounds composition blur bounds on Windows 11)
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                // Suppress DWM native window border on Windows 11
                int borderColor = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

                var accent = new AccentPolicy();
                accent.AccentState = enable ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND : AccentState.ACCENT_DISABLED;
                accent.GradientColor = 0x01FFFFFF; // Fully transparent tint overlay

                int accentStructSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData();
                data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
                data.SizeOfData = accentStructSize;
                data.Data = accentPtr;

                SetWindowCompositionAttribute(hWnd, ref data);

                Marshal.FreeHGlobal(accentPtr);
            }
            catch (Exception ex)
            {
                LogHelper.Log($"[DesktopWindowHelper] Failed to apply blur state: {ex.Message}");
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        /// <summary>
        /// Clips the native window boundaries to a rounded rectangle of the specified corner radius.
        /// This ensures the composition blur does not stick out of the rounded card corners.
        /// </summary>
        public static void SetRoundedWindowRegion(Window window, int cornerRadius)
        {
            return; // Disabled to prevent jagged non-anti-aliased clipping gaps
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;

                var source = PresentationSource.FromVisual(window);
                double dpiX = 1.0;
                double dpiY = 1.0;
                if (source?.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }

                int scaledWidth = (int)(window.ActualWidth * dpiX);
                int scaledHeight = (int)(window.ActualHeight * dpiY);
                int scaledRadiusX = (int)(cornerRadius * dpiX);
                int scaledRadiusY = (int)(cornerRadius * dpiY);

                IntPtr hRgn = CreateRoundRectRgn(0, 0, scaledWidth, scaledHeight, scaledRadiusX * 2, scaledRadiusY * 2);
                SetWindowRgn(hWnd, hRgn, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DesktopWindowHelper] Failed to set window region: {ex.Message}");
            }
        }
    }
}
