using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Kotak.Models;
using Kotak.Services;
using Microsoft.Win32;

namespace Kotak.Bridge;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class JsBridge
{
    private readonly MainWindow _mainWindow;
    private readonly AppConfigService _configService;
    private readonly AppLauncherService _launcherService;
    private readonly WifiService _wifiService;
    private readonly SystemService _systemService;
    private readonly FileExplorerService _fileExplorerService;
    private readonly ProcessManagerService _processManagerService;
    private readonly SimpleTransferService _transferService;

    public JsBridge(MainWindow mainWindow, AppConfigService configService)
    {
        _mainWindow = mainWindow;
        _configService = configService;
        _launcherService = new AppLauncherService();
        _wifiService = new WifiService();
        _systemService = new SystemService();
        _fileExplorerService = new FileExplorerService();
        _processManagerService = new ProcessManagerService();
        _transferService = new SimpleTransferService();

        // Wire up transfer events to send to WebView
        _transferService.OnLogEntry += (entry) =>
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.SendTransferLog(entry.Time.ToString("HH:mm:ss"), entry.Message, entry.IsError);
            });
        };
        _transferService.OnStatusChanged += (isRunning) =>
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.SendTransferStatus(isRunning);
            });
        };
    }

    // ============================
    // App Management
    // ============================

    /// <summary>
    /// Get all apps from app.json as JSON string
    /// </summary>
    public string GetApps()
    {
        var apps = _configService.GetApps();
        return JsonSerializer.Serialize(apps);
    }

    /// <summary>
    /// Add a new app to the configuration
    /// </summary>
    public bool AddApp(string name, string type, string pathOrUrl, string thumbnail)
    {
        return _configService.AddApp(name, type, pathOrUrl, thumbnail);
    }

    /// <summary>
    /// Remove an app from the configuration
    /// </summary>
    public bool RemoveApp(string name)
    {
        return _configService.RemoveApp(name);
    }

    /// <summary>
    /// Launch an app by name
    /// </summary>
    public string LaunchApp(string name)
    {
        try
        {
            var app = _configService.GetAppByName(name);
            if (app == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "App not found" });
            }

            if (app.Type.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                // Web apps: Navigate WebView2 to the URL (embedded browser)
                if (string.IsNullOrEmpty(app.Url))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Web app URL is empty" });
                }
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.NavigateToWebApp(app.Url));
            }
            else
            {
                // EXE apps: Launch externally as before
                _launcherService.Launch(app);
            }
            return JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Close the currently active web app and return to launcher
    /// </summary>
    public void CloseWebApp()
    {
        _mainWindow.Dispatcher.Invoke(() => _mainWindow.NavigateToLauncher());
    }

    /// <summary>
    /// Open file dialog to browse for EXE file
    /// </summary>
    public string BrowseForExe()
    {
        string? result = null;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select Application"
            };

            if (dialog.ShowDialog() == true)
            {
                result = dialog.FileName;
            }
        });

        if (result != null)
        {
            // Get suggested app name and extract icon
            var appName = _launcherService.GetAppNameFromPath(result);
            var thumbnailPath = _launcherService.ExtractIconFromExe(result, _configService.ThumbnailsPath, appName);

            return JsonSerializer.Serialize(new
            {
                path = result,
                suggestedName = appName,
                thumbnail = thumbnailPath != null ? $"thumbnails/{Path.GetFileName(thumbnailPath)}" : ""
            });
        }

        return JsonSerializer.Serialize(new { path = (string?)null });
    }

    // ============================
    // Wi-Fi Management
    // ============================

    /// <summary>
    /// Get available Wi-Fi networks as JSON string
    /// </summary>
    public string GetWifiNetworks()
    {
        var networks = _wifiService.ScanNetworks();
        return JsonSerializer.Serialize(networks);
    }

    /// <summary>
    /// Connect to a Wi-Fi network
    /// </summary>
    public bool ConnectToWifi(string ssid, string password)
    {
        return _wifiService.Connect(ssid, password);
    }

    /// <summary>
    /// Get current Wi-Fi connection status
    /// </summary>
    public string GetWifiStatus()
    {
        var currentSsid = _wifiService.GetCurrentConnection();
        return JsonSerializer.Serialize(new
        {
            connected = !string.IsNullOrEmpty(currentSsid),
            ssid = currentSsid
        });
    }

    /// <summary>
    /// Disconnect from current Wi-Fi
    /// </summary>
    public bool DisconnectWifi()
    {
        return _wifiService.Disconnect();
    }

    // ============================
    // System Operations
    // ============================

    /// <summary>
    /// Shutdown the PC
    /// </summary>
    public void ShutdownPC()
    {
        _systemService.Shutdown();
    }

    /// <summary>
    /// Restart the PC
    /// </summary>
    public void RestartPC()
    {
        _systemService.Restart();
    }

    /// <summary>
    /// Put PC to sleep
    /// </summary>
    public void SleepPC()
    {
        _systemService.Sleep();
    }

    /// <summary>
    /// Exit the launcher application
    /// </summary>
    public void ExitLauncher()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.Close();
        });
    }

    // ============================
    // Volume & Brightness Control
    // ============================

    /// <summary>
    /// Get current system volume (0-100)
    /// </summary>
    public int GetVolume()
    {
        return _systemService.GetVolume();
    }

    /// <summary>
    /// Set system volume (0-100)
    /// </summary>
    public void SetVolume(int level)
    {
        _systemService.SetVolume(level);
    }

    /// <summary>
    /// Get current screen brightness (0-100)
    /// </summary>
    public int GetBrightness()
    {
        return _systemService.GetBrightness();
    }

    /// <summary>
    /// Set screen brightness (0-100)
    /// </summary>
    public bool SetBrightness(int level)
    {
        return _systemService.SetBrightness(level);
    }

    /// <summary>
    /// Check if brightness control is supported (laptops only)
    /// </summary>
    public bool IsBrightnessSupported()
    {
        return _systemService.IsBrightnessSupported();
    }

    // ============================
    // Settings
    // ============================

    /// <summary>
    /// Check if auto-start is enabled
    /// </summary>
    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("Kotak") != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enable or disable auto-start
    /// </summary>
    public bool SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return false;

            if (enabled)
            {
                // Use Environment.ProcessPath for single-file app compatibility
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue("Kotak", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("Kotak", false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get thumbnails folder path for the WebUI
    /// </summary>
    public string GetThumbnailsPath()
    {
        return _configService.ThumbnailsPath;
    }

    // ============================
    // Controller Configuration
    // ============================

    /// <summary>
    /// Get current controller button mapping as JSON
    /// </summary>
    public string GetControllerConfig()
    {
        var config = _configService.GetControllerConfig();
        return JsonSerializer.Serialize(new
        {
            buttonA = config.ButtonA,
            buttonB = config.ButtonB,
            buttonX = config.ButtonX,
            buttonY = config.ButtonY,
            buttonLB = config.ButtonLB,
            buttonRB = config.ButtonRB,
            buttonBack = config.ButtonBack,
            buttonStart = config.ButtonStart,
            buttonLStick = config.ButtonLStick,
            buttonRStick = config.ButtonRStick
        });
    }

    /// <summary>
    /// Set a specific controller button mapping
    /// </summary>
    public bool SetControllerButton(string buttonName, uint rawValue)
    {
        try
        {
            _configService.SetControllerButton(buttonName, rawValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if gamepad is connected
    /// </summary>
    public bool IsGamepadConnected()
    {
        return _mainWindow.IsGamepadConnected();
    }

    /// <summary>
    /// Start controller mapping mode - returns raw button values via message
    /// </summary>
    public void StartControllerMapping()
    {
        _mainWindow.StartControllerMappingMode();
    }

    /// <summary>
    /// Stop controller mapping mode
    /// </summary>
    public void StopControllerMapping()
    {
        _mainWindow.StopControllerMappingMode();
    }

    // ============================
    // File Explorer
    // ============================

    /// <summary>
    /// Get list of available drives
    /// </summary>
    public string GetDrives()
    {
        var drives = _fileExplorerService.GetDrives();
        return JsonSerializer.Serialize(drives);
    }

    /// <summary>
    /// Get contents of a directory
    /// </summary>
    public string GetDirectoryContents(string path)
    {
        var contents = _fileExplorerService.GetDirectoryContents(path);
        return JsonSerializer.Serialize(contents);
    }

    /// <summary>
    /// Get user's home directory
    /// </summary>
    public string GetUserHome()
    {
        return _fileExplorerService.GetUserHome();
    }

    /// <summary>
    /// Get user's Downloads folder
    /// </summary>
    public string GetDownloadsFolder()
    {
        return _fileExplorerService.GetDownloadsFolder();
    }

    /// <summary>
    /// Open a file with default application
    /// </summary>
    public bool OpenFile(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return false;

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================
    // Running Apps Management
    // ============================

    /// <summary>
    /// Get list of running apps that are registered in config
    /// </summary>
    public string GetRunningApps()
    {
        var registeredApps = _configService.GetApps();
        var runningApps = _processManagerService.GetRunningApps(registeredApps);
        return JsonSerializer.Serialize(runningApps);
    }

    /// <summary>
    /// Kill a running process by ID
    /// </summary>
    public bool KillProcess(int processId)
    {
        return _processManagerService.KillProcess(processId);
    }

    /// <summary>
    /// Bring a process window to foreground
    /// </summary>
    public bool FocusProcess(int processId)
    {
        return _processManagerService.FocusProcess(processId);
    }

    // ============================
    // Simple Transfer
    // ============================

    /// <summary>
    /// Start the transfer server
    /// </summary>
    public string StartTransferServer()
    {
        var success = _transferService.Start();
        var ip = _transferService.GetLocalIpAddress();
        var port = _transferService.Port;

        return JsonSerializer.Serialize(new
        {
            success,
            url = success && ip != null ? $"http://{ip}:{port}" : null,
            port
        });
    }

    /// <summary>
    /// Stop the transfer server
    /// </summary>
    public void StopTransferServer()
    {
        _transferService.Stop();
    }

    /// <summary>
    /// Check if transfer server is running
    /// </summary>
    public bool IsTransferServerRunning()
    {
        return _transferService.IsRunning;
    }

    /// <summary>
    /// Get transfer server save path
    /// </summary>
    public string GetTransferSavePath()
    {
        return _transferService.SavePath;
    }

    /// <summary>
    /// Set transfer server save path
    /// </summary>
    public bool SetTransferSavePath(string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            _transferService.SetSavePath(path);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Browse for folder (for transfer save location) using OpenFolderDialog
    /// </summary>
    public string? BrowseForFolder()
    {
        string? result = null;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            // Use OpenFolderDialog (available in .NET 8 WPF)
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder to save received files",
                InitialDirectory = _transferService.SavePath
            };

            if (dialog.ShowDialog() == true)
            {
                result = dialog.FolderName;
            }
        });

        return result;
    }
}
