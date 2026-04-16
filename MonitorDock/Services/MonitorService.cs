using WinForms = System.Windows.Forms;

namespace MonitorDock.Services;

public class MonitorInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public System.Drawing.Rectangle Bounds { get; set; }
    public System.Drawing.Rectangle WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
}

public static class MonitorService
{
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
            monitors.Add(new MonitorInfo
            {
                Id = screen.DeviceName,
                Name = $"Monitor {i + 1}",
                Bounds = screen.Bounds,
                WorkingArea = screen.WorkingArea,
                IsPrimary = screen.Primary
            });
        }
        return monitors;
    }
}
