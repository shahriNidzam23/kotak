# KOTAK

A fullscreen, TV-friendly launcher for Windows 10/11 Mini PCs. Built with WPF (.NET 8) and WebView2. Coded by Claude Code.

## Features

- **Gamepad Support** - Full XInput controller navigation (Xbox, Fantech, etc.)
- **App Launcher** - Launch web apps and native EXE applications
- **Dynamic App Management** - Add/remove apps without rebuilding
- **Wi-Fi Management** - Scan and connect to networks
- **System Controls** - Shutdown, restart, sleep from the launcher

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
kotak.bat clean     # Clean build outputs
```

## Controls

### Gamepad
| Button | Action |
|--------|--------|
| D-pad / Left Stick | Navigate |
| A | Select |
| B | Back |
| Y | Add App |
| X | Remove App (on tile) |
| LB / RB | Switch tabs |
| Start | Settings Menu |

### Keyboard
| Key | Action |
|-----|--------|
| Arrow Keys | Navigate |
| Enter | Select |
| Escape | Back |
| Y | Add App |
| Delete | Remove App (on tile) |
| Space | Settings Menu |

## Configuration

Apps are stored in `config.json`:

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
  }
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
│   │   └── WifiNetwork.cs
│   ├── Services/
│   │   ├── AppConfigService.cs
│   │   ├── AppLauncherService.cs
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
