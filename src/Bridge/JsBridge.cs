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
    private readonly UpdateService _updateService;
    private readonly IptvService _iptvService;

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
        _updateService = new UpdateService();
        _iptvService = new IptvService();

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
    /// Reload config from disk (refresh without restart)
    /// </summary>
    public bool ReloadConfig()
    {
        return _configService.ReloadConfig();
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

    /// <summary>
    /// Show desktop by minimizing all windows
    /// </summary>
    public void ShowDesktop()
    {
        _systemService.ShowDesktop();
    }

    /// <summary>
    /// Open Microsoft Edge browser
    /// </summary>
    public void OpenBrowser()
    {
        _systemService.OpenBrowser();
    }

    /// <summary>
    /// Start Tailscale VPN
    /// </summary>
    public string StartTailscale()
    {
        var result = _systemService.StartTailscale();
        return JsonSerializer.Serialize(new { success = result.success, message = result.message });
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

    /// <summary>
    /// Get the application version
    /// </summary>
    public string GetVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    /// <summary>
    /// Get the Web Remote URL for QR code display
    /// </summary>
    public string GetWebRemoteUrl()
    {
        return _mainWindow.GetWebRemoteUrl() ?? "";
    }

    /// <summary>
    /// Broadcast UI state to Web Remote clients for Mirror view sync
    /// </summary>
    public void BroadcastUIState(string screen, string tab, int focusedIndex, string appsJson)
    {
        _mainWindow.BroadcastUIState(screen, tab, focusedIndex, appsJson);
    }

    /// <summary>
    /// Get README.md content for About screen
    /// </summary>
    public string GetReadmeContent()
    {
        try
        {
            // Check multiple locations for README.md
            var possiblePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "README.md"),
                Path.Combine(AppContext.BaseDirectory, "..", "README.md"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "README.md")
            };

            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return File.ReadAllText(fullPath);
                }
            }

            return "README.md not found.";
        }
        catch (Exception ex)
        {
            return $"Error reading README: {ex.Message}";
        }
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

    // ============================
    // Update Management
    // ============================

    /// <summary>
    /// Check for updates from GitHub releases
    /// </summary>
    public string CheckForUpdates()
    {
        try
        {
            var task = _updateService.CheckForUpdatesAsync();
            task.Wait();
            var result = task.Result;
            return JsonSerializer.Serialize(new
            {
                hasUpdate = result.HasUpdate,
                currentVersion = result.CurrentVersion,
                latestVersion = result.LatestVersion,
                releaseNotes = result.ReleaseNotes,
                releaseName = result.ReleaseName,
                releaseUrl = result.ReleaseUrl,
                downloadUrl = result.DownloadUrl,
                fileName = result.FileName,
                fileSize = result.FileSize,
                publishedAt = result.PublishedAt,
                error = result.Error
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { hasUpdate = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Open the GitHub releases page in browser
    /// </summary>
    public void OpenReleasesPage()
    {
        _updateService.OpenReleasesPage();
    }

    // Store last update info for download
    private UpdateInfo? _lastUpdateInfo;
    private CancellationTokenSource? _downloadCts;

    /// <summary>
    /// Start downloading and installing the update
    /// </summary>
    public string StartUpdateDownload()
    {
        try
        {
            // Get update info
            var task = _updateService.CheckForUpdatesAsync();
            task.Wait();
            _lastUpdateInfo = task.Result;

            if (!_lastUpdateInfo.HasUpdate || string.IsNullOrEmpty(_lastUpdateInfo.DownloadUrl))
            {
                return JsonSerializer.Serialize(new { success = false, error = "No update available" });
            }

            // Cancel any existing download
            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();

            // Start async download
            _ = DownloadAndInstallUpdateAsync(_lastUpdateInfo.DownloadUrl, _lastUpdateInfo.FileName ?? "update.zip");

            return JsonSerializer.Serialize(new { success = true, message = "Download started" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private async Task DownloadAndInstallUpdateAsync(string downloadUrl, string fileName)
    {
        try
        {
            // Phase 1: Download
            SendUpdateProgress("download", 0, "Starting download...");

            var downloadPath = await _updateService.DownloadUpdateAsync(
                downloadUrl,
                fileName,
                progress => SendUpdateProgress("download", progress, $"Downloading... {progress}%")
            );

            if (string.IsNullOrEmpty(downloadPath))
            {
                SendUpdateError("Download failed. Please try again.");
                return;
            }

            SendUpdateProgress("download", 100, "Download complete!");

            // Phase 2: Extract
            SendUpdateProgress("extract", 0, "Extracting update...");

            var extractPath = await _updateService.ExtractUpdateAsync(
                downloadPath,
                status => SendUpdateProgress("extract", 50, status)
            );

            if (string.IsNullOrEmpty(extractPath))
            {
                SendUpdateError("Failed to extract update files.");
                return;
            }

            SendUpdateProgress("extract", 100, "Extraction complete!");

            // Phase 3: Generate batch script
            SendUpdateProgress("install", 0, "Preparing installation...");

            var batchPath = _updateService.GenerateUpdateBatchScript(extractPath);

            if (string.IsNullOrEmpty(batchPath))
            {
                SendUpdateError("Failed to prepare update installer.");
                return;
            }

            SendUpdateProgress("install", 50, "Ready to install. KOTAK will restart...");

            // Give UI time to update
            await Task.Delay(1500);

            // Phase 4: Launch script and close app
            if (_updateService.LaunchUpdateScript(batchPath))
            {
                SendUpdateProgress("install", 100, "Installing update...");
                await Task.Delay(500);

                // Close the application
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow.Close();
                });
            }
            else
            {
                SendUpdateError("Failed to start update installer.");
            }
        }
        catch (Exception ex)
        {
            SendUpdateError($"Update failed: {ex.Message}");
        }
    }

    private void SendUpdateProgress(string phase, int progress, string message)
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.SendUpdateProgress(phase, progress, message);
        });
    }

    private void SendUpdateError(string error)
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.SendUpdateError(error);
        });
    }

    /// <summary>
    /// Cancel ongoing update download
    /// </summary>
    public void CancelUpdateDownload()
    {
        _downloadCts?.Cancel();
    }

    // ============================
    // IPTV Management
    // ============================

    /// <summary>
    /// Get all IPTV playlists
    /// </summary>
    public string GetIptvPlaylists()
    {
        try
        {
            var playlists = _configService.GetIptvPlaylists();
            return JsonSerializer.Serialize(playlists.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                url = p.Url,
                lastUpdated = p.LastUpdated,
                channelCount = p.Channels.Count,
                failedChannelIds = p.FailedChannelIds
            }));
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get channels for a specific playlist
    /// </summary>
    public string GetPlaylistChannels(string playlistId)
    {
        try
        {
            var playlist = _configService.GetIptvPlaylistById(playlistId);
            if (playlist == null)
            {
                return JsonSerializer.Serialize(new { error = "Playlist not found" });
            }

            var failedIds = playlist.FailedChannelIds ?? new List<string>();
            return JsonSerializer.Serialize(playlist.Channels.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                url = c.Url,
                logo = c.Logo,
                group = c.Group,
                failed = failedIds.Contains(c.Id)
            }));
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // Store for background IPTV add task results
    private static string? _pendingIptvResult = null;
    private static bool _iptvAddInProgress = false;

    /// <summary>
    /// Add a new IPTV playlist from M3U URL (runs in background)
    /// </summary>
    public string AddIptvPlaylist(string name, string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Name and URL are required" });
            }

            // Check if already in progress
            if (_iptvAddInProgress)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Another playlist is being added. Please wait." });
            }

            // Check for duplicate name before starting background task
            if (_configService.GetIptvPlaylists().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return JsonSerializer.Serialize(new { success = false, error = "A playlist with this name already exists" });
            }

            // Start background task
            _iptvAddInProgress = true;
            _pendingIptvResult = null;

            Task.Run(async () =>
            {
                try
                {
                    // Parse the M3U playlist
                    var playlist = await _iptvService.ParsePlaylistAsync(name, url);

                    if (playlist == null)
                    {
                        _pendingIptvResult = JsonSerializer.Serialize(new { success = false, error = "Failed to parse playlist. Check the URL.", name });
                        return;
                    }

                    if (playlist.Channels.Count == 0)
                    {
                        _pendingIptvResult = JsonSerializer.Serialize(new { success = false, error = "No channels found in playlist", name });
                        return;
                    }

                    // Save to config
                    if (!_configService.AddIptvPlaylist(playlist))
                    {
                        _pendingIptvResult = JsonSerializer.Serialize(new { success = false, error = "A playlist with this name already exists", name });
                        return;
                    }

                    _pendingIptvResult = JsonSerializer.Serialize(new
                    {
                        success = true,
                        playlist = new
                        {
                            id = playlist.Id,
                            name = playlist.Name,
                            url = playlist.Url,
                            lastUpdated = playlist.LastUpdated,
                            channelCount = playlist.Channels.Count
                        }
                    });
                }
                catch (Exception ex)
                {
                    _pendingIptvResult = JsonSerializer.Serialize(new { success = false, error = ex.Message, name });
                }
                finally
                {
                    _iptvAddInProgress = false;
                }
            });

            // Return immediately indicating task started
            return JsonSerializer.Serialize(new { started = true, name });
        }
        catch (Exception ex)
        {
            _iptvAddInProgress = false;
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Check if IPTV playlist add operation is complete
    /// </summary>
    public string CheckIptvAddResult()
    {
        if (_iptvAddInProgress)
        {
            return JsonSerializer.Serialize(new { pending = true });
        }

        if (_pendingIptvResult != null)
        {
            var result = _pendingIptvResult;
            _pendingIptvResult = null;
            return result;
        }

        return JsonSerializer.Serialize(new { pending = false, noResult = true });
    }

    /// <summary>
    /// Remove an IPTV playlist
    /// </summary>
    public string RemoveIptvPlaylist(string playlistId)
    {
        try
        {
            var success = _configService.RemoveIptvPlaylist(playlistId);
            return JsonSerializer.Serialize(new { success });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    // Store for background IPTV refresh task results
    private static string? _pendingRefreshResult = null;
    private static bool _iptvRefreshInProgress = false;

    /// <summary>
    /// Refresh an IPTV playlist (re-fetch from URL) - runs in background
    /// </summary>
    public string RefreshIptvPlaylist(string playlistId)
    {
        try
        {
            var playlist = _configService.GetIptvPlaylistById(playlistId);
            if (playlist == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Playlist not found" });
            }

            // Check if already in progress
            if (_iptvRefreshInProgress)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Another refresh is in progress. Please wait." });
            }

            // Start background task
            _iptvRefreshInProgress = true;
            _pendingRefreshResult = null;
            var playlistName = playlist.Name;
            var playlistUrl = playlist.Url;

            Task.Run(async () =>
            {
                try
                {
                    // Re-fetch and parse
                    var channels = await _iptvService.RefreshPlaylistAsync(playlistUrl);

                    if (channels == null)
                    {
                        _pendingRefreshResult = JsonSerializer.Serialize(new { success = false, error = "Failed to refresh playlist", name = playlistName });
                        return;
                    }

                    // Update playlist
                    var currentPlaylist = _configService.GetIptvPlaylistById(playlistId);
                    if (currentPlaylist != null)
                    {
                        currentPlaylist.Channels = channels;
                        currentPlaylist.LastUpdated = DateTime.UtcNow;
                        _configService.UpdateIptvPlaylist(currentPlaylist);
                    }

                    _pendingRefreshResult = JsonSerializer.Serialize(new
                    {
                        success = true,
                        channelCount = channels.Count,
                        name = playlistName
                    });
                }
                catch (Exception ex)
                {
                    _pendingRefreshResult = JsonSerializer.Serialize(new { success = false, error = ex.Message, name = playlistName });
                }
                finally
                {
                    _iptvRefreshInProgress = false;
                }
            });

            // Return immediately indicating task started
            return JsonSerializer.Serialize(new { started = true, name = playlistName });
        }
        catch (Exception ex)
        {
            _iptvRefreshInProgress = false;
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Check if IPTV playlist refresh operation is complete
    /// </summary>
    public string CheckIptvRefreshResult()
    {
        if (_iptvRefreshInProgress)
        {
            return JsonSerializer.Serialize(new { pending = true });
        }

        if (_pendingRefreshResult != null)
        {
            var result = _pendingRefreshResult;
            _pendingRefreshResult = null;
            return result;
        }

        return JsonSerializer.Serialize(new { pending = false, noResult = true });
    }

    /// <summary>
    /// Mark a channel as failed (not working)
    /// </summary>
    public string MarkChannelFailed(string playlistId, string channelId)
    {
        try
        {
            var success = _configService.MarkChannelFailed(playlistId, channelId);
            return JsonSerializer.Serialize(new { success });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Clear the failed status of a channel
    /// </summary>
    public string ClearChannelFailed(string playlistId, string channelId)
    {
        try
        {
            var success = _configService.ClearChannelFailed(playlistId, channelId);
            return JsonSerializer.Serialize(new { success });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }
}
