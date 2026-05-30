<p align="center">
  <img src="assets/logo.png" alt="GameTools logo" width="128" height="128">
</p>

<h1 align="center">GameTools</h1>

<p align="center">
  A Windows utility for optimizing the gaming experience — window management, cursor
  confinement, audio control, virtual gamepad, and a phone remote. Built with .NET 9.0 WinForms.
</p>

## Features

- **Window Capture** — Capture any window with a 5-second countdown, then apply your settings automatically
- **Cursor Confinement** — Lock the mouse cursor inside the game window; auto-releases on focus loss
- **Border Removal** — Strip window borders and title bars for a cleaner borderless look
- **Window Centering & Resizing** — Center the game on your monitor and resize to a custom resolution (default 2560×1440)
- **Black Background** — Overlay to hide the desktop behind your game window
- **Audio Mute on Alt+Tab** — Automatically mute the game when you switch away, unmute when you return
- **FPS Overlay** — Real-time framerate display using DWM composition timing
- **Virtual Gamepad** — Emulate an Xbox controller via ViGEmBus; maps raw mouse motion to the right analog stick for aim
- **Game Profiles** — Save per-game settings that persist across sessions. Applying settings now also saves the profile automatically
- **Favorites & Auto-Detect** — Mark profiles as favorites to auto-apply settings when the game launches. Favoriting an already-open game applies instantly
- **Remote Control** — Companion phone app controls GameTools over your local network (HTTP on port 9876, auto-discovered via UDP)
- **Global Hotkey** — Trigger capture from anywhere (default: `Ctrl+Alt+G`, customizable)
- **Start with Windows** — Optional startup entry via the Windows registry
- **System Tray** — Minimize to tray to keep it out of the way; with tray mode on, closing the window hides it instead of quitting

## Requirements

- Windows 10/11 (64-bit)
- .NET 9.0 Runtime (or use the self-contained release)
- [ViGEmBus](https://github.com/nefarius/ViGEmBus) driver — only required for the Virtual Gamepad feature

## Installation

Download `GameTools.exe` from the `publish/` folder and run it. No installer needed — it's a single self-contained executable (~49 MB, bundles the .NET runtime).

## Building from Source

```bash
dotnet publish -c Release -o publish
```

This produces a compressed, single-file, self-contained `publish/GameTools.exe`. The publish
settings (single file, compression, self-contained, win-x64) live in `GameTools.csproj`, so the
plain command above is all you need. VS Code users can also run the **Generate Release** task.

## Configuration

Settings and profiles are stored as JSON files next to the executable:

| File | Purpose |
|------|---------|
| `gametools_settings.json` | Hotkey, default toggles, resolution |
| `gametools_profiles.json` | Per-game profiles and favorites |

## Project Structure

```
GameTools/
├── assets/         # Application icon and logo
├── Core/           # Win32 wrappers, window/audio control, gamepad emulation, web server
├── Data/           # Profiles, settings, JSON serialization
├── UI/             # Main form, FPS overlay, theming (Catppuccin Mocha dark theme)
├── Layout/         # Flexbox-like layout helpers for WinForms
├── Program.cs      # Entry point (single-instance enforcement)
└── GameTools.csproj
```

## License

All rights reserved.
</content>
