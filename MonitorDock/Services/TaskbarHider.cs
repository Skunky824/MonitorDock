using System.Runtime.InteropServices;
using System.Text;

namespace MonitorDock.Services;

public static class TaskbarHider
{
    public static void HideSecondaryTaskbars()
    {
        // The main taskbar window
        var primaryTaskbar = FindWindow("Shell_TrayWnd", null);

        // Secondary taskbars are "Shell_SecondaryTrayWnd" windows
        IntPtr hwnd = IntPtr.Zero;
        while (true)
        {
            hwnd = FindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null);
            if (hwnd == IntPtr.Zero) break;
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    public static void ShowSecondaryTaskbars()
    {
        IntPtr hwnd = IntPtr.Zero;
        while (true)
        {
            hwnd = FindWindowEx(IntPtr.Zero, hwnd, "Shell_SecondaryTrayWnd", null);
            if (hwnd == IntPtr.Zero) break;
            ShowWindow(hwnd, SW_SHOW);
        }
    }

    public static bool IsPrimaryTaskbarAutoHide()
    {
        var abd = new APPBARDATA();
        abd.cbSize = Marshal.SizeOf<APPBARDATA>();
        var state = SHAppBarMessage(ABM_GETSTATE, ref abd);
        return (state.ToInt32() & ABS_AUTOHIDE) != 0;
    }

    public static void SetPrimaryTaskbarAutoHide(bool autoHide)
    {
        var abd = new APPBARDATA();
        abd.cbSize = Marshal.SizeOf<APPBARDATA>();
        abd.lParam = autoHide ? (IntPtr)ABS_AUTOHIDE : IntPtr.Zero;
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint ABM_GETSTATE = 0x04;
    private const uint ABM_SETSTATE = 0x0A;
    private const int ABS_AUTOHIDE = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
