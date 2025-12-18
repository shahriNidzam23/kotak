# KOTAK

A fullscreen, TV-friendly launcher for Windows 10/11 Mini PCs. Built with WPF (.NET 8) and WebView2. Coded by Claude Code.

## Why was KOTAK developed?

1. **Turn any Mini PC into a TV Box** - Use your Windows Mini PC as a dedicated media center with a couch-friendly UI optimized for TV viewing and gamepad navigation
2. **Lightweight App Launcher** - Simple, fast, and focused. No bloat, just launch your apps
3. **Because I can** - Similar apps exist, but I love to procrastinate productively

## Features

- **Gamepad Support** - Full XInput controller navigation (Xbox, Fantech, etc.)
- **App Launcher** - Launch web apps and native EXE applications
- **Dynamic App Management** - Add/remove apps without rebuilding
- **IPTV Support** - Add M3U/M3U8 playlists to watch live TV channels
- **Wi-Fi Management** - Scan and connect to networks
- **System Controls** - Shutdown, restart, sleep from the launcher
- **Utilities** - File explorer, file transfer, browser, Tailscale VPN

## Requirements

- Windows 10/11 (x64)
- .NET 8.0 SDK (for building)
- WebView2 Runtime (usually pre-installed on Windows 10/11)

## Quick Start

### Build and Run

```cmd
kotak.bat run
```

### Other Commands

```cmd
kotak.bat run       # Build and run
kotak.bat publish   # Create Release executable
kotak.bat release   # Publish and create GitHub release
kotak.bat version   # Show current version
kotak.bat clean     # Clean build outputs
```

## Controls

### Gamepad
| Button | Action |
|--------|--------|
| D-pad / Left Stick | Navigate |
| A | Select |
| B | Back |
| Y | Add App / Add IPTV Playlist |
| X | Remove App (on tile) |
| LB / RB | Switch tabs |
| Start | Settings Menu |

### Keyboard
| Key | Action |
|-----|--------|
| Arrow Keys | Navigate |
| Enter | Select |
| Escape | Back |
| Y | Add App / Add IPTV Playlist |
| Delete | Remove App (on tile) |
| Space | Settings Menu |

## Configuration

Apps and IPTV playlists are stored in `config.json`:

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
      "path": "C:\\Program Files\\VideoLAN\\VLC\\vlc.exe",
      "thumbnail": "thumbnails/vlc.png"
    }
  ],
  "controller": {
    "buttonA": 1,
    "buttonB": 2,
    ...
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

### App Entry Fields

| Field | Description |
|-------|-------------|
| `name` | Display name |
| `type` | `web` or `exe` |
| `url` | URL for web apps |
| `path` | File path for EXE apps |
| `thumbnail` | Path to icon image |
| `arguments` | (Optional) Launch arguments |

## Project Structure

```
kotak/
├── kotak.bat             # Build script
├── config.json           # App configuration (created at runtime)
├── thumbnails/           # App icons
├── src/
│   ├── Kotak.csproj
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Models/
│   │   ├── AppConfig.cs
│   │   ├── IptvModels.cs
│   │   └── WifiNetwork.cs
│   ├── Services/
│   │   ├── AppConfigService.cs
│   │   ├── AppLauncherService.cs
│   │   ├── IptvService.cs
│   │   ├── RawInputGamepadService.cs
│   │   ├── WifiService.cs
│   │   └── SystemService.cs
│   ├── Bridge/
│   │   └── JsBridge.cs
│   └── WebUI/
│       ├── index.html
│       ├── css/styles.css
│       └── js/app.js
└── README.md
```

## Deployment

For deployment, run:

```cmd
kotak.bat publish
```

This creates a `publish/` folder containing:
- `Kotak.exe` - Single-file executable
- `WebUI/` - UI files

Copy the entire `publish/` folder to your Mini PC. `config.json` and `thumbnails/` will be created automatically on first run.

## Adding Apps

### Via UI
1. Press **Y** or go to Settings > Add Application
2. Click **Browse** to select an EXE
3. Edit the name if needed
4. Click **Add**

### Via config.json
Manually edit `config.json` and add entries following the format above.

## Customization

### Adding Custom Thumbnails
Place PNG/JPG images in the `thumbnails/` folder and reference them in `config.json`.

## Updates

### Checking for Updates
1. Go to **Settings** tab
2. Click **Check for Updates**
3. If an update is available, click **Download Update** to open the releases page
4. Download the new `Kotak.exe` and replace the old one

### Creating a Release (Developers)

To create a new GitHub release:

```cmd
kotak.bat release
```

This command:
1. Prompts for version type (Major/Minor/Patch)
2. Builds the Release executable
3. Commits and tags the version
4. Pushes to GitHub
5. Creates a GitHub release with `Kotak.exe` attached

**Prerequisites:**
- [GitHub CLI](https://cli.github.com/) installed
- Authenticated: run `gh auth login`

## Troubleshooting

### WebView2 not found
Install the [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### Gamepad not working
- Ensure controller is connected before launching
- Controller Settings allows remapping buttons
- Check Device Manager for driver issues

### Wi-Fi not showing
- Requires Wi-Fi adapter
- Some operations may need administrator privileges

## License

MIT License


## TODO
- Add Radio streaming support
- Add USB TV remote support
- Gamepad control over web apps

