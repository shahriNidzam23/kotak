# Product Requirements Document (PRD) – KOTAK TV Launcher

## 1. Project Overview

**Product Name:** KOTAK
**Platform:** Windows 10/11 (Mini PC connected to TV)
**Target Users:** Anyone using a mini PC + TV, including remote or gamepad control
**Current Version:** 0.0.0

### Purpose

Create a **fullscreen, TV-friendly launcher** built with **WPF (.NET 8) and WebView2** that allows users to navigate and launch applications, manage apps dynamically, connect to Wi-Fi, and control the mini PC — all with a couch-friendly UI. Includes **gamepad support** for a console-like experience.

### Why was KOTAK developed?

1. **Turn any Mini PC into a TV Box** - Use your Windows Mini PC as a dedicated media center with a couch-friendly UI optimized for TV viewing and gamepad navigation
2. **Lightweight App Launcher** - Simple, fast, and focused. No bloat, just launch your apps
3. **Because I can** - Similar apps exist, but I love to procrastinate productively

---

## 2. Goals & Objectives

### Primary Goals

* Provide a **couch-friendly UI** optimized for TV viewing
* Allow navigation using arrow keys, remote, or **gamepad controller (XInput/Fantech)**
* Launch web apps and native EXE applications
* Dynamically add apps from any folder
* Manage Wi-Fi connectivity
* Control system (shutdown, restart, sleep)
* Use **WPF + WebView2** for native performance and flexible UI

### Success Criteria

* User can launch any configured app within **2 clicks**
* Launcher starts automatically on login (optional)
* Full TV-friendly UI with gamepad/remote support
* Apps can be added dynamically without rebuilding
* Single EXE deployment (~69MB self-contained)
* Built-in update checker

---

## 3. Core Features

### Implemented Features

| Feature                  | Status | Description                                                                                     |
| ------------------------ | ------ | ----------------------------------------------------------------------------------------------- |
| **Grid Browser UI**      | Done   | Fullscreen grid displaying apps with name + thumbnail. Couch-friendly interface via WebView2.  |
| **Open Selected App**    | Done   | Launch any app (web or EXE) directly from launcher.                                             |
| **Add Apps Dynamically** | Done   | Browse any folder, select any EXE, automatically add it to grid.                                |
| **Shutdown/Restart/Sleep** | Done | Access to turn off, restart, or sleep the mini PC from Settings tab.                           |
| **Wi-Fi Connectivity**   | Done   | Scan for networks and connect to Wi-Fi directly from launcher.                                  |
| **Gamepad Support**      | Done   | Full navigation via XInput-compatible controllers. Remappable buttons. Web app scroll/click. Right stick mouse control.    |
| **Volume Control**       | Done   | Adjust system volume from Settings tab.                                                         |
| **Controller Remapping** | Done   | Customize gamepad button mappings via Settings.                                                 |
| **Update Checker**       | Done   | Check for updates from GitHub releases.                                                         |
| **Version Display**      | Done   | Show current version in Settings tab.                                                           |
| **File Explorer**        | Done   | Browse files and folders from Utilities tab.                                                    |
| **File Transfer**        | Done   | Transfer files between devices via Utilities tab.                                               |
| **Config Refresh**       | Done   | Reload config.json without restarting the app via Settings.                                     |
| **IPTV Support**         | Done   | Add M3U/M3U8 playlists to watch live TV channels with channel management.                       |
| **Browser Utility**      | Done   | Launch Microsoft Edge browser from Utilities tab.                                               |
| **Tailscale VPN**        | Done   | Turn on Tailscale VPN from Utilities tab.                                                       |
| **Failed Channel Tracking** | Done | Automatically mark channels that fail to play and display them as disabled.                   |

### Planned Features (TODO)

| Feature                        | Priority | Description                                                    |
| ------------------------------ | -------- | -------------------------------------------------------------- |
| **Radio Streaming**            | Medium   | Add support for internet radio streams.                        |
| **Error Handling & Logging**   | Medium   | Improve error handling and add logging for debugging.          |
| **TV Remote Support**          | Low      | Support for IR/Bluetooth TV remotes.                           |
| **Auto-Update**                | Low      | Download and install updates automatically.                    |

---

## 4. App Configuration (config.json)

* JSON-based configuration file stores apps, controller settings, and IPTV playlists
* Auto-generated on first run with default apps
* Each app entry contains:
  * name
  * type (web / exe)
  * url or executable path
  * thumbnail image path
  * arguments (optional)
* IPTV playlists contain:
  * id, name, url
  * channels (parsed from M3U)
  * failedChannelIds (auto-tracked)

Example:

```json
{
  "apps": [
    {
      "name": "Netflix",
      "type": "web",
      "url": "https://www.netflix.com",
      "thumbnail": "thumbnails/netflix.png"
    },
    {
      "name": "VLC",
      "type": "exe",
      "path": "C:/Program Files/VideoLAN/VLC/vlc.exe",
      "thumbnail": "thumbnails/vlc.png",
      "arguments": "--fullscreen"
    }
  ],
  "controller": {
    "buttonA": 2,
    "buttonB": 4,
    "buttonX": 1,
    "buttonY": 8,
    "buttonLB": 16,
    "buttonRB": 32,
    "buttonBack": 64,
    "buttonStart": 128,
    "buttonLStick": 256,
    "buttonRStick": 512
  },
  "iptvPlaylists": [
    {
      "id": "uuid",
      "name": "My IPTV",
      "url": "https://example.com/playlist.m3u",
      "lastUpdated": "2025-01-01T00:00:00Z",
      "channels": [...],
      "failedChannelIds": []
    }
  ]
}
```

---

## 5. UI/UX Design

* Fullscreen window (no borders, taskbar hidden)
* Tab-based layout: **Apps**, **IPTV**, **Utilities**, **Settings**
* Grid layout with large tiles rendered in **WebView2**
* Highlight/focus indicator for currently selected tile
* Navigation: Arrow keys, D-pad, or analog stick
* **LB/RB** for tab switching
* Header with date/time and Wi-Fi status

### Controls

| Gamepad | Keyboard | Action |
|---------|----------|--------|
| D-pad / Left Stick | Arrow Keys | Navigate / Scroll (in web apps) |
| Right Stick | - | Mouse cursor control |
| A | Enter | Select / Mouse click (when using right stick) |
| B | Escape | Back / Browser Back (in web apps) |
| X | Delete / X | Remove App / Close web app |
| Y | Y | Add App / Add IPTV Playlist |
| LB / RB | - | Switch tabs |
| LB + RB + Start | Ctrl+Esc | Close web app |
| Start | Space | Settings Menu |

---

## 6. Technical Architecture

* **WPF (.NET 8)**: native Windows performance, handles window, input, and system commands
* **WebView2**: renders flexible HTML/CSS/JS UI for the app grid
* **Self-contained EXE**: ~69MB, includes .NET runtime (no installation required)
* **C# backend** handles:
  * Launching apps (EXE or web)
  * Browsing directories and adding apps
  * Shutdown/restart/sleep functionality
  * Wi-Fi management (scan, connect)
  * Volume control
  * Gamepad polling and button remapping
  * Update checking via GitHub API
  * IPTV playlist parsing and channel management
  * Tailscale VPN control

### Project Structure

```
kotak/
├── kotak.bat             # Build script (run, publish, release, version, clean)
├── config.json           # App configuration (auto-generated)
├── thumbnails/           # App icons (auto-generated)
├── src/
│   ├── Kotak.csproj
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Models/           # AppConfig, IptvModels, WifiNetwork
│   ├── Services/         # AppConfig, Launcher, Gamepad, Wifi, System, Update, IPTV
│   ├── Bridge/           # JsBridge (C# <-> JavaScript)
│   └── WebUI/            # HTML, CSS, JS for UI
└── README.md
```

---

## 7. Build & Release

### Build Commands

```cmd
kotak.bat run       # Build and run (Debug)
kotak.bat publish   # Create Release executable (prompts for version)
kotak.bat release   # Publish and create GitHub release
kotak.bat version   # Show current version
kotak.bat clean     # Clean build outputs
```

### Release Process

1. Run `kotak.bat release`
2. Select version type (Major/Minor/Patch)
3. Build creates `publish/` folder
4. Script commits, tags, and pushes to GitHub
5. Creates GitHub release with zip (excludes config.json and thumbnails)

### Update Process (Users)

1. Go to Settings tab
2. Click "Check for Updates"
3. If update available, click "Download Update"
4. Download zip from GitHub releases
5. Extract and replace files (config.json preserved)

---

## 8. Non-Functional Requirements

| Requirement | Target |
|-------------|--------|
| Startup time | < 3 seconds |
| Memory usage | < 150MB |
| Executable size | ~69MB (self-contained) |
| Offline support | Yes (except updates, Wi-Fi) |
| TV-friendly | High contrast, readable from 2-3 meters |

---

## 9. Milestones

### Completed

- [x] MVP Grid UI + config.json integration using WPF + WebView2
- [x] Launch web/EXE apps
- [x] Dynamic add apps via folder browsing
- [x] Shutdown/Restart/Sleep buttons
- [x] Wi-Fi connectivity interface
- [x] Gamepad navigation support with remapping
- [x] Volume control
- [x] File explorer and transfer utilities
- [x] Update checker from GitHub releases
- [x] Version display in Settings
- [x] Full deployment package / self-contained EXE
- [x] Automated GitHub release via kotak.bat
- [x] Config refresh without restart
- [x] IPTV / TV channel support (M3U/M3U8 playlists)
- [x] Failed channel tracking (auto-marks broken channels)
- [x] Browser utility (Microsoft Edge)
- [x] Tailscale VPN utility

### Future

- [ ] Radio streaming support
- [ ] Theming support (dark/light)
- [ ] Auto-update (download and install automatically)
- [ ] TV remote support
- [ ] Page navigation for large app lists

---

**End of PRD**
