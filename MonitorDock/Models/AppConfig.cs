namespace MonitorDock.Models;

public class MonitorPins
{
    public string MonitorId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsPrimary { get; set; }
    public int BoundsWidth { get; set; }
    public int BoundsHeight { get; set; }
    public List<PinnedApp> PinnedApps { get; set; } = new();
}

public class AppConfig
{
    public List<MonitorPins> Monitors { get; set; } = new();
    public int DockHeight { get; set; } = 48;
    public int IconSize { get; set; } = 24;
    public bool ClickFocusedMinimizes { get; set; } = true;
    public bool HideSecondaryTaskbars { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
}
