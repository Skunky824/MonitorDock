using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace MonitorDock.Services;

public class MonitorInfo
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Name { get; set; } = "";
    public System.Drawing.Rectangle Bounds { get; set; }
    public System.Drawing.Rectangle WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
}

public static class MonitorService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var screens = WinForms.Screen.AllScreens
            .OrderBy(s => !s.Primary)
            .ThenBy(s => s.Bounds.X)
            .ThenBy(s => s.Bounds.Y)
            .ToList();

        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var stableId = GetStableMonitorId(screen.DeviceName);
            monitors.Add(new MonitorInfo
            {
                Id = stableId ?? screen.DeviceName,
                DeviceName = screen.DeviceName,
                Name = $"Monitor {i + 1}",
                Bounds = screen.Bounds,
                WorkingArea = screen.WorkingArea,
                IsPrimary = screen.Primary
            });
        }

        // Disambiguate monitors with the same hardware ID by appending an ordinal
        var duplicateIds = monitors.GroupBy(m => m.Id).Where(g => g.Count() > 1);
        foreach (var group in duplicateIds)
        {
            int ordinal = 0;
            foreach (var monitor in group)
            {
                monitor.Id = $"{monitor.Id}#{ordinal++}";
            }
        }

        return monitors;
    }

    private static string? GetStableMonitorId(string adapterDeviceName)
    {
        var dd = new DISPLAY_DEVICE();
        dd.cb = Marshal.SizeOf(dd);

        // Enumerate monitor devices attached to this display adapter
        if (EnumDisplayDevices(adapterDeviceName, 0, ref dd, 0))
        {
            // DeviceID looks like: MONITOR\DELA0EC\{guid}\instance
            // Extract the hardware part (MONITOR\DELA0EC) which is EDID-based and stable
            var deviceId = dd.DeviceID;
            if (!string.IsNullOrEmpty(deviceId))
            {
                var parts = deviceId.Split('\\');
                if (parts.Length >= 2)
                    return $"{parts[0]}\\{parts[1]}";
            }
        }
        return null;
    }
}
