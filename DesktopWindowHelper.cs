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

        [DllImport("user32.dll")]
        private static extern bool GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        // Constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        // SetWindowPos Constants
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const uint GA_ROOT = 2;

        private const uint WM_SPAWN_WORKER = 0x052C;

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

        /// <summary>
        /// Enables frosted glass blur effect (Acrylic) behind a window.
        /// </summary>
        public static void EnableBlur(Window window)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;

                // Natively round window corners (rounds composition blur bounds on Windows 11)
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                var accent = new AccentPolicy();
                accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
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
                LogHelper.Log($"[DesktopWindowHelper] Failed to enable blur: {ex.Message}");
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
