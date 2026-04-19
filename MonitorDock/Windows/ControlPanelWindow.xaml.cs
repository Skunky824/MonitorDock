using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonitorDock.Models;
using MonitorDock.Services;

namespace MonitorDock.Windows;

public class MonitorViewModel
{
    public string MonitorId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool Enabled { get; set; } = true;
    public int BoundsWidth { get; set; }
    public int BoundsHeight { get; set; }
    public ObservableCollection<PinnedApp> PinnedApps { get; set; } = new();
}

public partial class ControlPanelWindow : Window
{
    public AppConfig Config { get; private set; }
    private readonly List<MonitorViewModel> _monitorViewModels = new();

    public ControlPanelWindow(AppConfig config)
    {
        // Deep copy the config so cancel discards changes
        Config = new AppConfig
        {
            DockHeight = config.DockHeight,
            IconSize = config.IconSize,
            ClickFocusedMinimizes = config.ClickFocusedMinimizes,
            HideSecondaryTaskbars = config.HideSecondaryTaskbars,
            StartWithWindows = config.StartWithWindows,
            Monitors = config.Monitors.Select(m => new MonitorPins
            {
                MonitorId = m.MonitorId,
                DeviceName = m.DeviceName,
                MonitorName = m.MonitorName,
                Enabled = m.Enabled,
                IsPrimary = m.IsPrimary,
                BoundsWidth = m.BoundsWidth,
                BoundsHeight = m.BoundsHeight,
                PinnedApps = m.PinnedApps.Select(a => new PinnedApp
                {
                    Name = a.Name,
                    Path = a.Path,
                    Arguments = a.Arguments
                }).ToList()
            }).ToList()
        };

        InitializeComponent();

        BarHeightSlider.Value = Config.DockHeight;
        BarHeightLabel.Text = $"{Config.DockHeight}px";
        IconSizeSlider.Value = Config.IconSize;
        IconSizeLabel.Text = $"{Config.IconSize}px";
        ClickFocusedMinimizesCheck.IsChecked = Config.ClickFocusedMinimizes;
        HideSecondaryTaskbarsCheck.IsChecked = Config.HideSecondaryTaskbars;
        StartWithWindowsCheck.IsChecked = Config.StartWithWindows;

        LoadMonitors();
    }

    private void LoadMonitors()
    {
        var monitors = MonitorService.GetMonitors();
        _monitorViewModels.Clear();

        foreach (var monitor in monitors)
        {
            var existing = Config.Monitors.FirstOrDefault(m => m.MonitorId == monitor.Id)
                ?? Config.Monitors.FirstOrDefault(m => !string.IsNullOrEmpty(m.DeviceName) && m.DeviceName == monitor.DeviceName)
                ?? Config.Monitors.FirstOrDefault(m => m.MonitorId.StartsWith(@"\\.\") && m.MonitorId == monitor.DeviceName);
            var vm = new MonitorViewModel
            {
                MonitorId = monitor.Id,
                DeviceName = monitor.DeviceName,
                DisplayName = monitor.IsPrimary ? $"{monitor.Name} (Primary)" : monitor.Name,
                IsPrimary = monitor.IsPrimary,
                Enabled = existing?.Enabled ?? true,
                BoundsWidth = monitor.Bounds.Width,
                BoundsHeight = monitor.Bounds.Height,
                PinnedApps = new ObservableCollection<PinnedApp>(existing?.PinnedApps ?? new List<PinnedApp>())
            };
            _monitorViewModels.Add(vm);
        }

        MonitorList.ItemsSource = _monitorViewModels;
        if (_monitorViewModels.Count > 0)
            MonitorList.SelectedIndex = 0;
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorViewModel vm)
        {
            AppList.ItemsSource = vm.PinnedApps;
            MonitorEnabledCheck.IsChecked = vm.Enabled;
        }
    }

    private void MonitorEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorViewModel vm)
        {
            vm.Enabled = MonitorEnabledCheck.IsChecked == true;
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is not MonitorViewModel vm)
        {
            System.Windows.MessageBox.Show("Please select a monitor first.", "MonitorDock");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                vm.PinnedApps.Add(new PinnedApp
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Path = filePath
                });
            }
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorViewModel vm && AppList.SelectedItem is PinnedApp app)
        {
            vm.PinnedApps.Remove(app);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorViewModel vm && AppList.SelectedItem is PinnedApp app)
        {
            int index = vm.PinnedApps.IndexOf(app);
            if (index > 0)
            {
                vm.PinnedApps.Move(index, index - 1);
                AppList.SelectedIndex = index - 1;
            }
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorViewModel vm && AppList.SelectedItem is PinnedApp app)
        {
            int index = vm.PinnedApps.IndexOf(app);
            if (index < vm.PinnedApps.Count - 1)
            {
                vm.PinnedApps.Move(index, index + 1);
                AppList.SelectedIndex = index + 1;
            }
        }
    }

    private void ApplyUIToConfig()
    {
        Config.DockHeight = (int)BarHeightSlider.Value;
        Config.IconSize = (int)IconSizeSlider.Value;
        Config.ClickFocusedMinimizes = ClickFocusedMinimizesCheck.IsChecked == true;
        Config.HideSecondaryTaskbars = HideSecondaryTaskbarsCheck.IsChecked == true;
        Config.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        // Build list from current monitors
        var currentMonitors = _monitorViewModels.Select(vm => new MonitorPins
        {
            MonitorId = vm.MonitorId,
            DeviceName = vm.DeviceName,
            MonitorName = vm.DisplayName,
            Enabled = vm.Enabled,
            IsPrimary = vm.IsPrimary,
            BoundsWidth = vm.BoundsWidth,
            BoundsHeight = vm.BoundsHeight,
            PinnedApps = vm.PinnedApps.ToList()
        }).ToList();

        // Preserve orphaned configs (disconnected monitors) so their pinned apps aren't lost
        var currentIds = new HashSet<string>(currentMonitors.Select(m => m.MonitorId));
        var orphaned = Config.Monitors.Where(m =>
            !currentIds.Contains(m.MonitorId) && m.PinnedApps.Count > 0).ToList();

        Config.Monitors = currentMonitors.Concat(orphaned).ToList();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplyUIToConfig();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        // Save current config to disk before restarting so the new instance picks it up
        ApplyUIToConfig();
        ConfigService.Save(Config);

        DialogResult = false;
        Close();

        // Start new instance, then shut down current one
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true
        });
        System.Windows.Application.Current.Shutdown();
    }

    private void BarHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BarHeightLabel != null)
            BarHeightLabel.Text = $"{(int)e.NewValue}px";
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IconSizeLabel != null)
            IconSizeLabel.Text = $"{(int)e.NewValue}px";
    }
}
