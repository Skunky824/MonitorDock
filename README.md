# MonitorDock

A lightweight Windows 11 taskbar replacement that gives each monitor its own independent taskbar with pinned apps and running window tracking.

![Windows 11](https://img.shields.io/badge/Windows%2011-0078D4?logo=windows11&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET%209-512BD4?logo=dotnet&logoColor=white)
![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)

## The Problem

Windows 11 removed the ability to show taskbar icons only on the monitor where an app is open. If you have 2–3 monitors, all your running apps pile up on every taskbar, making it hard to find what you need at a glance.

## The Solution

MonitorDock replaces the taskbar on your secondary monitors (and optionally your primary monitor) with a clean, minimal dock that shows:

- **Pinned apps** — your favorite apps, configured per monitor
- **Running windows** — only the windows that are actually on that monitor
- **Focused app highlighting** — instantly see which window is active
- **Clock and settings** — quick access on every screen

## Features

- **Per-monitor pinned apps** — Pin different apps to each monitor's taskbar
- **Smart window tracking** — Running apps appear only on the monitor they're on
- **Drag to reorder** — Rearrange running app buttons by dragging
- **Right-click context menu** — Pin apps or close windows from the taskbar
- **Click-to-minimize** — Clicking a focused app minimizes it (configurable)
- **Hide Windows taskbars** — Automatically hides the default secondary taskbars
- **Primary monitor mode** — Use MonitorDock on your primary monitor too, with automatic Windows taskbar auto-hide
- **Configurable appearance** — Adjust bar height (32–72px) and icon size (16–48px)
- **Taskbar filtering** — Windows on secondary monitors are hidden from the primary Windows taskbar
- **Start with Windows** — Launch automatically on login
- **System tray** — Lives in the tray, double-click to open the Control Panel
- **Inno Setup installer** — Easy install/uninstall with optional startup registration

## Screenshots

*Coming soon*

## Installation

### Installer (recommended)

Download `MonitorDock-Setup-1.0.0.exe` from the [Releases](../../releases) page and run it. The installer will:
- Install to `Program Files\MonitorDock`
- Optionally create a desktop shortcut
- Optionally add to Windows startup

### Build from source

Requirements:
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (Windows)

```powershell
cd MonitorDock
dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o ..\publish
```

To build the installer, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and compile `MonitorDock-Setup.iss`.

## Usage

1. **Launch MonitorDock** — it appears in the system tray
2. **Right-click the tray icon** → Control Panel to configure
3. **Pin apps** — Select a monitor, click "Add App", browse to an `.exe` or `.lnk`
4. **Adjust appearance** — Use the sliders for bar height and icon size
5. **Enable behaviors** — Toggle click-to-minimize, hide secondary taskbars, start with Windows, or primary monitor mode

### Single Monitor Setup

MonitorDock works on a single monitor too. Enable **"Show dock on primary monitor"** in the Control Panel — this automatically sets the Windows taskbar to auto-hide and places a MonitorDock bar at the bottom of your screen.

## How It Works

- Each dock registers as a Windows **AppBar** (same API the real taskbar uses), reserving screen space at the bottom of each monitor
- Running windows are detected via `EnumWindows` and assigned to monitors using `MonitorFromWindow`
- The **TaskbarFilter** service uses the `ITaskbarList` COM interface to hide secondary-monitor windows from the primary Windows taskbar
- Secondary Windows taskbars (`Shell_SecondaryTrayWnd`) are hidden via `ShowWindow`
- Primary taskbar auto-hide is controlled via `SHAppBarMessage(ABM_SETSTATE)`

## Project Structure

```
MonitorDock/
├── Models/
│   ├── AppConfig.cs          # Configuration model (monitors, appearance, behavior)
│   └── PinnedApp.cs          # Pinned application model
├── Services/
│   ├── ConfigService.cs      # JSON config persistence (%AppData%\MonitorDock\)
│   ├── MonitorService.cs     # Multi-monitor enumeration
│   ├── TaskbarFilter.cs      # ITaskbarList COM — hides windows from primary taskbar
│   ├── TaskbarHider.cs       # Show/hide Windows taskbars, auto-hide control
│   ├── StartupService.cs     # Registry-based startup management
│   └── WindowTracker.cs      # EnumWindows-based running window detection
├── Windows/
│   ├── DockWindow.xaml/.cs   # The taskbar replacement (AppBar, drag reorder, context menu)
│   └── ControlPanelWindow.xaml/.cs  # Settings UI
├── Converters/
│   └── IconConverter.cs      # Exe path → icon converter
├── App.xaml/.cs              # Tray icon, lifecycle, dock management
└── MonitorDock.csproj
```

## Configuration

Settings are stored in `%AppData%\MonitorDock\config.json`. Crash logs are written to `%AppData%\MonitorDock\crash.log`.

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.
