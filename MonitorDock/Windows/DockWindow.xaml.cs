using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MonitorDock.Models;
using MonitorDock.Services;

namespace MonitorDock.Windows;

public partial class DockWindow : Window, INotifyPropertyChanged
{
    private readonly MonitorInfo _monitor;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _clockTimer;
    private bool _appBarRegistered;
    private readonly int _taskbarHeight;
    private readonly bool _clickFocusedMinimizes;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private RunningWindow? _dragSourceItem;

    public string MonitorName { get; }

    public int PinnedIconSize { get; }
    public int RunningIconSize { get; }

    private List<PinnedApp> _pinnedApps = new();
    public List<PinnedApp> PinnedApps
    {
        get => _pinnedApps;
        set { _pinnedApps = value; OnPropertyChanged(nameof(PinnedApps)); }
    }

    private ObservableCollection<RunningWindow> _runningWindows = new();
    public ObservableCollection<RunningWindow> RunningWindows
    {
        get => _runningWindows;
        set { _runningWindows = value; OnPropertyChanged(nameof(RunningWindows)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? OpenControlPanel;
    public event Action<string, string>? PinAppRequested; // exePath, name

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DockWindow(MonitorInfo monitor, List<PinnedApp> pinnedApps, int dockHeight, int iconSize = 24, bool clickFocusedMinimizes = true)
    {
        _monitor = monitor;
        _pinnedApps = pinnedApps;
        _taskbarHeight = dockHeight;
        _clickFocusedMinimizes = clickFocusedMinimizes;
        PinnedIconSize = iconSize;
        RunningIconSize = Math.Max(iconSize - 4, 16);
        MonitorName = monitor.IsPrimary ? $"{monitor.Name} (Primary)" : monitor.Name;

        DataContext = this;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (s, e) => RefreshRunningWindows();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (s, e) => UpdateClock();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hook WndProc for AppBar callback messages
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);

        SetExtendedWindowStyle();
        RegisterAppBar();
        PositionAsTaskbar();

        RefreshRunningWindows();
        UpdateClock();
        _refreshTimer.Start();
        _clockTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _clockTimer.Stop();
        UnregisterAppBar();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APPBAR_CALLBACK)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    // Another appbar or the taskbar changed — reposition ourselves
                    PositionAsTaskbar();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void RegisterAppBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = WM_APPBAR_CALLBACK
        };

        SHAppBarMessage(ABM_NEW, ref abd);
        _appBarRegistered = true;
    }

    private void PositionAsTaskbar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var bounds = _monitor.Bounds;

        // Step 1: Propose our desired position to the AppBar system
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uEdge = ABE_BOTTOM,
            rc = new RECT
            {
                Left = bounds.Left,
                Top = bounds.Bottom - _taskbarHeight,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            }
        };

        // Step 2: Let the system adjust our rect (QUERYPOS)
        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // Step 3: Fix up the rect based on the edge
        abd.rc.Top = abd.rc.Bottom - _taskbarHeight;

        // Step 4: Actually reserve the space (SETPOS)
        SHAppBarMessage(ABM_SETPOS, ref abd);

        // Step 5: Move our window to the reserved area
        SetWindowPos(hwnd, HWND_TOPMOST,
            abd.rc.Left, abd.rc.Top,
            abd.rc.Right - abd.rc.Left,
            abd.rc.Bottom - abd.rc.Top,
            SWP_NOACTIVATE);
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _appBarRegistered = false;
    }

    private void SetExtendedWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void RefreshRunningWindows()
    {
        var current = WindowTracker.GetWindowsOnMonitor(_monitor);
        var currentByHandle = current.ToDictionary(w => w.Handle);

        // Remove windows that are no longer present
        for (int i = _runningWindows.Count - 1; i >= 0; i--)
        {
            if (!currentByHandle.ContainsKey(_runningWindows[i].Handle))
                _runningWindows.RemoveAt(i);
        }

        // Add new windows (at the end) and update existing ones in-place
        foreach (var w in current)
        {
            var existing = _runningWindows.FirstOrDefault(e => e.Handle == w.Handle);
            if (existing == null)
            {
                // New window — append at the end to preserve order
                _runningWindows.Add(w);
            }
            else if (existing.Title != w.Title || existing.IsFocused != w.IsFocused)
            {
                // Update in-place — keeps the same position
                var idx = _runningWindows.IndexOf(existing);
                _runningWindows[idx] = w;
            }
        }
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm\ndd/MM");
    }

    private void PinnedApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: PinnedApp app })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to launch {app.Name}:\n{ex.Message}",
                    "MonitorDock", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RunningWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _dragSourceItem = null;
            return;
        }
        if (sender is System.Windows.Controls.Button { Tag: RunningWindow win })
        {
            if (_clickFocusedMinimizes)
                WindowTracker.ToggleMinimize(win.Handle);
            else
                WindowTracker.ActivateWindow(win.Handle);
        }
    }

    private void RunningApp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(RunningAppsPanel);
        _isDragging = false;
        if (sender is System.Windows.Controls.Button { Tag: RunningWindow win })
            _dragSourceItem = win;
    }

    private void RunningApps_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceItem == null) return;

        var pos = e.GetPosition(RunningAppsPanel);
        var diff = pos - _dragStartPoint;

        if (!_isDragging)
        {
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                Mouse.Capture(RunningAppsPanel);
            }
            return;
        }

        // Find the item under the cursor and swap
        var targetItem = FindRunningWindowAtPoint(pos);
        if (targetItem != null && targetItem.Handle != _dragSourceItem.Handle)
        {
            var oldIndex = IndexOfHandle(_dragSourceItem.Handle);
            var newIndex = IndexOfHandle(targetItem.Handle);
            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                _runningWindows.Move(oldIndex, newIndex);
        }
    }

    private void RunningApps_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            Mouse.Capture(null);
            e.Handled = true;
        }
        _dragSourceItem = null;
    }

    private RunningWindow? FindRunningWindowAtPoint(System.Windows.Point pt)
    {
        var hit = VisualTreeHelper.HitTest(RunningAppsPanel, pt);
        if (hit?.VisualHit == null) return null;

        var element = hit.VisualHit as FrameworkElement;
        while (element != null && element != RunningAppsPanel)
        {
            if (element is System.Windows.Controls.Button { Tag: RunningWindow rw })
                return rw;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return null;
    }

    private int IndexOfHandle(IntPtr handle)
    {
        for (int i = 0; i < _runningWindows.Count; i++)
            if (_runningWindows[i].Handle == handle) return i;
        return -1;
    }

    private void PinToBar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            mi.Parent is System.Windows.Controls.ContextMenu ctx &&
            ctx.PlacementTarget is System.Windows.Controls.Button { Tag: RunningWindow win })
        {
            PinAppRequested?.Invoke(win.ExePath, win.ProcessName);
        }
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            mi.Parent is System.Windows.Controls.ContextMenu ctx &&
            ctx.PlacementTarget is System.Windows.Controls.Button { Tag: RunningWindow win })
        {
            WindowTracker.CloseWindow(win.Handle);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenControlPanel?.Invoke();
    }

    #region Win32 Interop
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOACTIVATE = 0x0010;

    // AppBar constants
    private const int ABM_NEW = 0;
    private const int ABM_REMOVE = 1;
    private const int ABM_QUERYPOS = 2;
    private const int ABM_SETPOS = 3;
    private const int ABE_BOTTOM = 3;
    private const int WM_APPBAR_CALLBACK = 0x0400 + 100;
    private const int ABN_POSCHANGED = 1;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }
    #endregion
}
