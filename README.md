# GameTools

A Windows utility for optimizing the gaming experience with window management, cursor confinement, audio control, and performance monitoring. Built with .NET 9.0 WinForms.

## Features

- **Window Capture** -- Capture any window with a 5-second countdown, then apply your settings automatically
- **Cursor Confinement** -- Lock the mouse cursor inside the game window; auto-releases on focus loss
- **Border Removal** -- Strip window borders and title bars for a cleaner borderless look
- **Window Centering & Resizing** -- Center the game on your monitor and resize to a custom resolution (default 2560x1440)
- **Black Background** -- Overlay to hide the desktop behind your game window
- **Audio Mute on Alt+Tab** -- Automatically mute the game when you switch away, unmute when you return
- **FPS Overlay** -- Real-time framerate display using DWM composition timing
- **Game Profiles** -- Save per-game settings that persist across sessions
- **Favorites & Auto-Detect** -- Mark profiles as favorites to auto-apply settings when the game launches
- **Global Hotkey** -- Trigger capture from anywhere (default: `Ctrl+Alt+G`, customizable)
- **Start with Windows** -- Optional startup entry via the Windows registry
- **System Tray** -- Minimize to tray to keep it out of the way

## Requirements

- Windows 10/11 (64-bit)
- .NET 9.0 Runtime (or use the self-contained release)

## Installation

Download `GameTools.exe` from the `release/` folder and run it. No installer needed -- it's a single self-contained executable.

## Building from Source

```bash
dotnet publish -c Release -r win-x64 --self-contained -o release/
```

## Configuration

Settings and profiles are stored as JSON files next to the executable:

| File | Purpose |
|------|---------|
| `gametools_settings.json` | Hotkey, default toggles, resolution |
| `gametools_profiles.json` | Per-game profiles and favorites |

## Project Structure

```
GameTools/
├── Core/           # Win32 API wrappers, window manipulation, audio control
├── Data/           # Profiles, settings, JSON serialization
├── UI/             # Main form, FPS overlay, theming (Catppuccin dark theme)
├── Layout/         # Flexbox-like layout helpers for WinForms
├── Program.cs      # Entry point (single-instance enforcement)
└── GameTools.csproj
```

## License

All rights reserved.
