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
        if (_config.ShowOnPrimaryMonitor)
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
            // Skip primary monitor unless ShowOnPrimaryMonitor is enabled
            if (monitor.IsPrimary && !_config.ShowOnPrimaryMonitor) continue;

            var monitorPins = _config.Monitors.FirstOrDefault(m => m.MonitorId == monitor.Id);
            var pinnedApps = monitorPins?.PinnedApps ?? new List<PinnedApp>();

            var dock = new DockWindow(monitor, pinnedApps, _config.DockHeight, _config.IconSize, _config.ClickFocusedMinimizes);
            dock.OpenControlPanel += ShowControlPanel;
            dock.PinAppRequested += (exePath, name) => PinAppToMonitor(monitor.Id, monitor.Name, exePath, name);
            dock.Show();
            _dockWindows.Add(dock);
        }
    }

    private void PinAppToMonitor(string monitorId, string monitorName, string exePath, string name)
    {
        var monitorPins = _config.Monitors.FirstOrDefault(m => m.MonitorId == monitorId);
        if (monitorPins == null)
        {
            monitorPins = new MonitorPins { MonitorId = monitorId, MonitorName = monitorName };
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
            if (_config.ShowOnPrimaryMonitor)
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
        if (_config.ShowOnPrimaryMonitor)
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
        if (_config.ShowOnPrimaryMonitor)
            TaskbarHider.SetPrimaryTaskbarAutoHide(_originalAutoHideState);

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}

