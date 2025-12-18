# Product Requirements Document (PRD) – KOTAK TV Launcher

## 1. Project Overview

**Product Name:** KOTAK
**Platform:** Windows 10/11 (Mini PC connected to TV)
**Target Users:** Anyone using a mini PC + TV, including remote or gamepad control

### Purpose

Create a **fullscreen, TV-friendly launcher** built with **WPF (.NET 8) and WebView2** that allows users to navigate and launch applications, manage apps dynamically, connect to Wi-Fi, and turn off the mini PC — all without admin/user restrictions. Includes **gamepad support** for a console-like experience.

---

## 2. Goals & Objectives

### Primary Goals

* Provide a **10-foot UI** optimized for TV viewing
* Allow navigation using arrow keys, remote, or **gamepad controller (XInput/Fantech)**
* Launch web apps and native EXE applications
* Dynamically add apps from any folder
* Manage Wi-Fi connectivity
* Shutdown or restart the mini PC
* Use **WPF + WebView2** for native performance and flexible UI

### Success Criteria

* User can launch any configured app within **2 clicks**
* Launcher starts automatically on login
* Full TV-friendly UI with gamepad/remote support
* Apps can be added dynamically without rebuilding
* Single EXE deployment with low memory usage

---

## 3. Core Features

| Feature                  | Description                                                                                                                                           |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Grid Browser UI**      | Fullscreen grid displaying apps with name + thumbnail. 10-foot TV interface, rendered via **WebView2** inside WPF.                                    |
| **Open Selected App**    | Launch any app (web or EXE) directly from launcher.                                                                                                   |
| **Add Apps Dynamically** | Browse any folder, select any EXE, automatically add it to grid (no folder restrictions).                                                             |
| **Shutdown/Restart PC**  | Access to turn off or restart the mini PC from launcher, implemented via native C# calls.                                                             |
| **Wi-Fi Connectivity**   | Scan for networks and connect to Wi-Fi directly from launcher using C# backend.                                                                       |
| **Gamepad Support**      | Full navigation and selection via XInput-compatible controllers (e.g., Fantech). Arrow keys, A/B buttons, Start/Select mapped to appropriate actions. |
| **WPF + WebView2 UI**    | Launcher UI built with **WPF** for performance and native Windows features; **WebView2** used for flexible HTML/CSS grid layout.                      |

---

## 4. App Configuration (config.json)

* JSON-based configuration file stores apps dynamically
* Each app entry contains:

  * name
  * type (web / exe)
  * url or executable path
  * thumbnail image path
* Launcher reads config.json and updates grid automatically when new apps are added

Example:

```json
{
  "apps": [
    {"name": "Netflix", "type": "web", "url": "https://www.netflix.com", "thumbnail": "thumbnails/netflix.png"},
    {"name": "Jellyfin", "type": "exe", "path": "C:/Program Files/Jellyfin/JellyfinMediaPlayer.exe", "thumbnail": "thumbnails/jellyfin.png"}
  ],
  "controller": {
    "buttonA": 1,
    "buttonB": 2
  }
}
```

---

## 5. UI/UX Design

* Fullscreen window (no borders, taskbar hidden)
* Tab-based layout: Apps, Utilities, Settings
* Grid layout with large tiles rendered in **WebView2**
* Highlight for currently focused tile
* Arrow keys, remote, and **gamepad** navigation
* LB/RB for tab switching

---

## 6. Technical Architecture

* **WPF (.NET 8)**: native Windows performance, handles window, input, and system commands
* **WebView2**: renders flexible HTML/CSS/JS UI for the app grid
* **C# backend** handles:

  * Launching apps (EXE or web)
  * Browsing directories and adding apps
  * Shutdown/restart functionality
  * Wi-Fi management
  * Polling **gamepad/XInput controllers** for navigation and selection

---

## 7. Functional Notes

1. **Grid Browser App**: Read config.json, display all apps in a tile grid using WebView2
2. **Open App**: EXE via `Process.Start`, Web via embedded WebView2 browser
3. **Add Apps Dynamically**: Browse any folder, select EXE(s), update config.json
4. **Shutdown/Restart**: Accessible from Settings tab via C# commands
5. **Wi-Fi Connectivity**: Scan and connect to available networks via C# backend
6. **Gamepad Support**: Navigate, launch, and access menus via XInput-compatible controllers, integrated in WPF

---

## 8. Non-Functional Requirements

* Startup time < 3s
* Fullscreen always on top
* Minimal memory usage (<150MB)
* Works offline
* Easy to add apps with no technical knowledge
* TV-friendly visuals, high contrast, readable from 2–3 meters
* Single executable deployment

---

## 9. Future Enhancements (Optional)

* Page navigation for large app lists
* Theming support (dark/light)
* Auto-update mechanism for launcher
* Volume/brightness overlay

---

## 10. Milestones

1. MVP Grid UI + config.json integration using **WPF + WebView2**
2. Launch web/EXE apps
3. Dynamic add apps via folder browsing
4. Shutdown/Restart button
5. Wi-Fi connectivity interface
6. Gamepad navigation support integrated in WPF
7. Full deployment package / self-contained EXE

---

**End of PRD**
