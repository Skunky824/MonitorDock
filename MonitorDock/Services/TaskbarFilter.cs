using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace MonitorDock.Services;

public class TaskbarFilter : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly HashSet<IntPtr> _hiddenWindows = new();
    private ITaskbarList? _taskbarList;
    private bool _disposed;

    public TaskbarFilter()
    {
        // Create COM instance of TaskbarList
        var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
        var iid = typeof(ITaskbarList).GUID;
        CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out var obj);
        _taskbarList = (ITaskbarList)obj;
        _taskbarList.HrInit();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (s, e) => Refresh();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Refresh()
    {
        if (_taskbarList == null) return;

        var primaryMonitor = MonitorService.GetMonitors().FirstOrDefault(m => m.IsPrimary);
        if (primaryMonitor == null) return;

        // Get all visible app windows across ALL monitors
        var allWindows = GetAllAppWindows();

        // Determine which windows are on secondary monitors
        var secondaryHandles = new HashSet<IntPtr>();
        foreach (var hwnd in allWindows)
        {
            if (IsOnPrimaryMonitor(hwnd, primaryMonitor))
                continue;
            secondaryHandles.Add(hwnd);
        }

        // Hide windows that moved to secondary monitors
        foreach (var hwnd in secondaryHandles)
        {
            if (_hiddenWindows.Add(hwnd))
            {
                try { _taskbarList.DeleteTab(hwnd); } catch { }
            }
        }

        // Restore windows that moved back to primary or were closed
        var toRestore = _hiddenWindows.Where(h => !secondaryHandles.Contains(h)).ToList();
        foreach (var hwnd in toRestore)
        {
            _hiddenWindows.Remove(hwnd);
            if (IsWindow(hwnd))
            {
                try { _taskbarList.AddTab(hwnd); } catch { }
            }
        }
    }

    private static bool IsOnPrimaryMonitor(IntPtr hwnd, MonitorInfo primary)
    {
        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return true; // assume primary if unknown

        return mi.rcMonitor.Left == primary.Bounds.Left &&
               mi.rcMonitor.Top == primary.Bounds.Top &&
               mi.rcMonitor.Right == primary.Bounds.Right &&
               mi.rcMonitor.Bottom == primary.Bounds.Bottom;
    }

    private static List<IntPtr> GetAllAppWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            IntPtr owner = GetWindow(hwnd, GW_OWNER);
            if (!isAppWindow && owner != IntPtr.Zero) return true;

            if (IsWindowCloaked(hwnd)) return true;

            // Must have a title
            if (GetWindowTextLength(hwnd) == 0) return true;

            windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int));
        return cloaked != 0;
    }

    public void RestoreAll()
    {
        if (_taskbarList == null) return;
        foreach (var hwnd in _hiddenWindows)
        {
            if (IsWindow(hwnd))
            {
                try { _taskbarList.AddTab(hwnd); } catch { }
            }
        }
        _hiddenWindows.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        RestoreAll();
        if (_taskbarList != null)
        {
            Marshal.ReleaseComObject(_taskbarList);
            _taskbarList = null;
        }
    }

    #region COM / Win32

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const uint GW_OWNER = 4;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter,
        int dwClsContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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
