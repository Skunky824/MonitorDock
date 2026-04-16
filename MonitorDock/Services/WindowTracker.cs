using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace MonitorDock.Services;

public class RunningWindow
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool IsMinimized { get; set; }
    public bool IsFocused { get; set; }
    public BitmapSource? Icon { get; set; }
}

public static class WindowTracker
{
    public static List<RunningWindow> GetWindowsOnMonitor(MonitorInfo monitor)
    {
        var results = new List<RunningWindow>();
        var monitorRect = monitor.Bounds;
        var foregroundHwnd = GetForegroundWindow();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            // Skip windows with no title
            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            // Skip tool windows and our own windows
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            // Must have WS_EX_APPWINDOW or be a top-level owned window with caption
            bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            IntPtr owner = GetWindow(hwnd, GW_OWNER);
            if (!isAppWindow && owner != IntPtr.Zero) return true;

            // Skip cloaked windows (virtual desktop / UWP hidden)
            if (IsWindowCloaked(hwnd)) return true;

            // Determine which monitor this window belongs to
            var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref mi)) return true;

            // Check if the monitor this window is on matches our target
            if (mi.rcMonitor.Left != monitorRect.Left ||
                mi.rcMonitor.Top != monitorRect.Top ||
                mi.rcMonitor.Right != monitorRect.Right ||
                mi.rcMonitor.Bottom != monitorRect.Bottom)
                return true;

            // Get process info
            GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            string exePath = "";
            BitmapSource? icon = null;

            try
            {
                var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
                exePath = proc.MainModule?.FileName ?? "";
            }
            catch { }

            // Extract icon
            try
            {
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (ico != null)
                    {
                        icon = Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        icon.Freeze();
                    }
                }
            }
            catch { }

            bool isMinimized = IsIconic(hwnd);
            bool isFocused = (hwnd == foregroundHwnd);

            results.Add(new RunningWindow
            {
                Handle = hwnd,
                Title = title,
                ProcessName = processName,
                ExePath = exePath,
                IsMinimized = isMinimized,
                IsFocused = isFocused,
                Icon = icon
            });

            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static void ActivateWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        SetForegroundWindow(hwnd);
    }

    public static void ToggleMinimize(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        else if (GetForegroundWindow() == hwnd)
        {
            ShowWindow(hwnd, SW_MINIMIZE);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    public static void CloseWindow(IntPtr hwnd)
    {
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        int cloaked = 0;
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
        return cloaked != 0;
    }

    #region Win32

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const uint GW_OWNER = 4;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_CLOAKED = 14;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion
}
