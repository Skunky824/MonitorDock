using System.Diagnostics;
using System.Drawing;
using System.Windows;
using MonitorDock.Models;
using MonitorDock.Services;
using MonitorDock.Windows;
using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace MonitorDock;

public partial class App : Application
{
    private WinForms.NotifyIcon? _trayIcon;
    private AppConfig _config = new();
    private readonly List<DockWindow> _dockWindows = new();
    private ControlPanelWindow? _controlPanel;
    private TaskbarFilter? _taskbarFilter;
    private bool _originalAutoHideState;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Kill any other running instances to avoid duplicate docks
        KillOtherInstances();

        DispatcherUnhandledException += (s, ex) =>
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorDock", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.WriteAllText(logPath, $"{DateTime.Now}\n{ex.Exception}");
            ex.Handled = false;
        };

        _config = ConfigService.Load();
        MigrateMonitorIds();

        // Sync startup setting with actual registry state (installer may have set it)
        var registryEnabled = StartupService.IsEnabled();
        if (_config.StartWithWindows != registryEnabled)
        {
            _config.StartWithWindows = registryEnabled;
            ConfigService.Save(_config);
        }

        CreateTrayIcon();
        RefreshDocks();

        if (_config.HideSecondaryTaskbars)
            TaskbarHider.HideSecondaryTaskbars();

        _originalAutoHideState = TaskbarHider.IsPrimaryTaskbarAutoHide();
        if (IsPrimaryMonitorEnabled())
            TaskbarHider.SetPrimaryTaskbarAutoHide(true);

        _taskbarFilter = new TaskbarFilter();
        _taskbarFilter.Start();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MonitorDock - Per-monitor pinned apps",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Items.Add("Control Panel", null, (s, e) => ShowControlPanel());
        _trayIcon.ContextMenuStrip.Items.Add("Refresh Monitors", null, (s, e) => RefreshDocks());
        _trayIcon.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => ExitApp());

        _trayIcon.DoubleClick += (s, e) => ShowControlPanel();
    }

    private void RefreshDocks()
    {
        foreach (var dock in _dockWindows)
            dock.Close();
        _dockWindows.Clear();

        var monitors = MonitorService.GetMonitors();

        foreach (var monitor in monitors)
        {
            var monitorPins = FindMonitorPins(monitor);

            // Check if this monitor is enabled in config (default: enabled)
            if (monitorPins != null && !monitorPins.Enabled) continue;

            var pinnedApps = monitorPins?.PinnedApps ?? new List<PinnedApp>();

            var dock = new DockWindow(monitor, pinnedApps, _config.DockHeight, _config.IconSize, _config.ClickFocusedMinimizes);
            dock.OpenControlPanel += ShowControlPanel;
            dock.PinAppRequested += (exePath, name) => PinAppToMonitor(monitor.Id, monitor.Name, exePath, name);
            dock.Show();
            _dockWindows.Add(dock);
        }

        // Update stored monitor metadata for future matching
        UpdateMonitorMetadata(monitors);
    }

    /// <summary>
    /// Finds the saved MonitorPins for a monitor using multi-level fallback:
    /// 1. Exact match on MonitorId (EDID-based, stable)
    /// 2. Match on legacy DeviceName (\\.\DISPLAY1)
    /// 3. Match on resolution + primary status
    /// </summary>
    private MonitorPins? FindMonitorPins(MonitorInfo monitor)
    {
        // Level 1: Exact ID match (new EDID-based ID)
        var match = _config.Monitors.FirstOrDefault(m => m.MonitorId == monitor.Id);
        if (match != null) return match;

        // Level 2: Match by DeviceName (legacy or current)
        match = _config.Monitors.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.DeviceName) && m.DeviceName == monitor.DeviceName);
        if (match != null)
        {
            match.MonitorId = monitor.Id; // migrate to new ID
            return match;
        }

        // Level 3: Match by old MonitorId that looks like a DeviceName (migration from old format)
        match = _config.Monitors.FirstOrDefault(m =>
            m.MonitorId.StartsWith(@"\\.\") && m.MonitorId == monitor.DeviceName);
        if (match != null)
        {
            match.MonitorId = monitor.Id; // migrate to new ID
            return match;
        }

        // Level 4: Match by resolution + primary status (for when all IDs changed)
        match = _config.Monitors.FirstOrDefault(m =>
            m.BoundsWidth == monitor.Bounds.Width &&
            m.BoundsHeight == monitor.Bounds.Height &&
            m.IsPrimary == monitor.IsPrimary &&
            m.PinnedApps.Count > 0);
        if (match != null)
        {
            match.MonitorId = monitor.Id; // adopt this monitor
            return match;
        }

        return null;
    }

    private void UpdateMonitorMetadata(List<MonitorInfo> monitors)
    {
        bool changed = false;
        foreach (var monitor in monitors)
        {
            var pins = _config.Monitors.FirstOrDefault(m => m.MonitorId == monitor.Id);
            if (pins != null)
            {
                if (pins.DeviceName != monitor.DeviceName ||
                    pins.BoundsWidth != monitor.Bounds.Width ||
                    pins.BoundsHeight != monitor.Bounds.Height ||
                    pins.IsPrimary != monitor.IsPrimary)
                {
                    pins.DeviceName = monitor.DeviceName;
                    pins.BoundsWidth = monitor.Bounds.Width;
                    pins.BoundsHeight = monitor.Bounds.Height;
                    pins.IsPrimary = monitor.IsPrimary;
                    changed = true;
                }
            }
        }
        if (changed) ConfigService.Save(_config);
    }

    private void PinAppToMonitor(string monitorId, string monitorName, string exePath, string name)
    {
        var monitorPins = _config.Monitors.FirstOrDefault(m => m.MonitorId == monitorId);
        if (monitorPins == null)
        {
            var monitor = MonitorService.GetMonitors().FirstOrDefault(m => m.Id == monitorId);
            monitorPins = new MonitorPins
            {
                MonitorId = monitorId,
                MonitorName = monitorName,
                DeviceName = monitor?.DeviceName ?? "",
                IsPrimary = monitor?.IsPrimary ?? false,
                BoundsWidth = monitor?.Bounds.Width ?? 0,
                BoundsHeight = monitor?.Bounds.Height ?? 0
            };
            _config.Monitors.Add(monitorPins);
        }

        // Don't add duplicates
        if (monitorPins.PinnedApps.Any(p => string.Equals(p.Path, exePath, StringComparison.OrdinalIgnoreCase)))
            return;

        monitorPins.PinnedApps.Add(new PinnedApp
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(exePath),
            Path = exePath
        });

        ConfigService.Save(_config);
        RefreshDocks();
    }

    private void ShowControlPanel()
    {
        if (_controlPanel != null && _controlPanel.IsVisible)
        {
            _controlPanel.Activate();
            return;
        }

        _controlPanel = new ControlPanelWindow(_config);
        if (_controlPanel.ShowDialog() == true)
        {
            _config = _controlPanel.Config;
            ConfigService.Save(_config);
            RefreshDocks();

            // Apply taskbar visibility
            if (_config.HideSecondaryTaskbars)
                TaskbarHider.HideSecondaryTaskbars();
            else
                TaskbarHider.ShowSecondaryTaskbars();

            // Apply primary taskbar auto-hide
            if (IsPrimaryMonitorEnabled())
                TaskbarHider.SetPrimaryTaskbarAutoHide(true);
            else
                TaskbarHider.SetPrimaryTaskbarAutoHide(_originalAutoHideState);

            // Apply startup setting
            StartupService.SetEnabled(_config.StartWithWindows);
        }
        _controlPanel = null;
    }

    private void ExitApp()
    {
        _taskbarFilter?.Dispose();
        TaskbarHider.ShowSecondaryTaskbars();
        if (IsPrimaryMonitorEnabled())
            TaskbarHider.SetPrimaryTaskbarAutoHide(_originalAutoHideState);

        foreach (var dock in _dockWindows)
            dock.Close();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarFilter?.Dispose();
        TaskbarHider.ShowSecondaryTaskbars();
        if (IsPrimaryMonitorEnabled())
            TaskbarHider.SetPrimaryTaskbarAutoHide(_originalAutoHideState);

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }

    private bool IsPrimaryMonitorEnabled()
    {
        var primaryId = MonitorService.GetMonitors().FirstOrDefault(m => m.IsPrimary)?.Id;
        if (primaryId == null) return false;
        var pins = _config.Monitors.FirstOrDefault(m => m.MonitorId == primaryId);
        return pins?.Enabled ?? false;
    }

    /// <summary>
    /// Migrates old DeviceName-based MonitorIds (\\.\DISPLAY1) to EDID-based IDs.
    /// Also populates DeviceName field if empty (upgrade from older config format).
    /// </summary>
    private void MigrateMonitorIds()
    {
        if (_config.Monitors.Count == 0) return;

        var monitors = MonitorService.GetMonitors();
        bool changed = false;

        foreach (var pins in _config.Monitors)
        {
            // Old format used DeviceName as MonitorId — migrate if we find a matching current monitor
            if (pins.MonitorId.StartsWith(@"\\.\"))
            {
                var monitor = monitors.FirstOrDefault(m => m.DeviceName == pins.MonitorId);
                if (monitor != null)
                {
                    pins.DeviceName = pins.MonitorId;
                    pins.MonitorId = monitor.Id;
                    pins.IsPrimary = monitor.IsPrimary;
                    pins.BoundsWidth = monitor.Bounds.Width;
                    pins.BoundsHeight = monitor.Bounds.Height;
                    changed = true;
                }
            }

            // Populate DeviceName if missing (older config versions)
            if (string.IsNullOrEmpty(pins.DeviceName))
            {
                var monitor = monitors.FirstOrDefault(m => m.Id == pins.MonitorId);
                if (monitor != null)
                {
                    pins.DeviceName = monitor.DeviceName;
                    pins.IsPrimary = monitor.IsPrimary;
                    pins.BoundsWidth = monitor.Bounds.Width;
                    pins.BoundsHeight = monitor.Bounds.Height;
                    changed = true;
                }
            }
        }

        if (changed) ConfigService.Save(_config);
    }

    private static void KillOtherInstances()
    {
        var currentId = Environment.ProcessId;
        var currentName = Process.GetCurrentProcess().ProcessName;

        foreach (var proc in Process.GetProcessesByName(currentName))
        {
            if (proc.Id != currentId)
            {
                try { proc.Kill(); } catch { }
            }
            proc.Dispose();
        }
    }
}

