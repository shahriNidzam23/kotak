using System.IO;
using System.Text.Json;
using Kotak.Models;

namespace Kotak.Services;

public class AppConfigService
{
    private readonly string _configPath;
    private readonly string _thumbnailsPath;
    private AppConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public event Action<ControllerConfig>? OnControllerConfigChanged;

    public AppConfigService()
    {
        // Use AppContext.BaseDirectory for single-file app compatibility
        var basePath = AppContext.BaseDirectory;

        // For development (running from bin/Debug or bin/Release), check if WebUI exists in parent
        // This indicates we're running from bin folder and should use project root
        var projectRoot = Path.GetFullPath(Path.Combine(basePath, ".."));
        var isDevMode = Directory.Exists(Path.Combine(basePath, "WebUI")) == false
                        && Directory.Exists(Path.Combine(projectRoot, "src", "WebUI"));

        if (isDevMode)
        {
            // Development: config in project root (parent of bin)
            _configPath = Path.Combine(projectRoot, "config.json");
            _thumbnailsPath = Path.Combine(projectRoot, "thumbnails");
        }
        else
        {
            // Production/Published: config next to executable
            _configPath = Path.Combine(basePath, "config.json");
            _thumbnailsPath = Path.Combine(basePath, "thumbnails");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureThumbnailsDirectory();
        _config = LoadConfig();
    }

    private AppConfig LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                if (config != null)
                {
                    // Ensure controller config exists
                    config.Controller ??= new ControllerConfig();
                    return config;
                }
            }
            catch { }
        }

        // Try to migrate from old app.json
        var oldConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "app.json");
        if (File.Exists(oldConfigPath))
        {
            try
            {
                var json = File.ReadAllText(oldConfigPath);
                var oldConfig = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                if (oldConfig != null)
                {
                    // Add default controller config if missing
                    oldConfig.Controller ??= new ControllerConfig();
                    SaveConfig(oldConfig);
                    return oldConfig;
                }
            }
            catch { }
        }

        return CreateDefaultConfig();
    }

    private AppConfig CreateDefaultConfig()
    {
        var config = new AppConfig
        {
            Apps = new List<AppEntry>
            {
                new AppEntry
                {
                    Name = "Netflix",
                    Type = "web",
                    Url = "https://www.netflix.com",
                    Thumbnail = "thumbnails/netflix.png"
                },
                new AppEntry
                {
                    Name = "YouTube",
                    Type = "web",
                    Url = "https://www.youtube.com",
                    Thumbnail = "thumbnails/youtube.png"
                },
                new AppEntry
                {
                    Name = "Disney+",
                    Type = "web",
                    Url = "https://www.disneyplus.com",
                    Thumbnail = "thumbnails/disney.png"
                }
            },
            Controller = new ControllerConfig()
        };

        SaveConfig(config);
        return config;
    }

    private void SaveConfig(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(_configPath, json);
        _config = config;
    }

    private void EnsureThumbnailsDirectory()
    {
        if (!Directory.Exists(_thumbnailsPath))
        {
            Directory.CreateDirectory(_thumbnailsPath);
        }
    }

    // ============================
    // App Management
    // ============================

    /// <summary>
    /// Reload config from disk (refresh without restart)
    /// </summary>
    public bool ReloadConfig()
    {
        try
        {
            _config = LoadConfig();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<AppEntry> GetApps()
    {
        return _config.Apps;
    }

    public AppEntry? GetAppByName(string name)
    {
        return _config.Apps.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public bool AddApp(string name, string type, string pathOrUrl, string? thumbnailPath)
    {
        // Check for duplicate
        if (_config.Apps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var app = new AppEntry
        {
            Name = name,
            Type = type.ToLower()
        };

        if (type.Equals("web", StringComparison.OrdinalIgnoreCase))
        {
            app.Url = pathOrUrl;
        }
        else
        {
            app.Path = pathOrUrl;
        }

        // Handle thumbnail
        if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
        {
            var thumbnailName = $"{SanitizeFileName(name)}{Path.GetExtension(thumbnailPath)}";
            var destPath = Path.Combine(_thumbnailsPath, thumbnailName);
            File.Copy(thumbnailPath, destPath, overwrite: true);
            app.Thumbnail = $"thumbnails/{thumbnailName}";
        }
        else
        {
            app.Thumbnail = thumbnailPath ?? string.Empty;
        }

        _config.Apps.Add(app);
        SaveConfig(_config);
        return true;
    }

    public bool RemoveApp(string name)
    {
        var app = _config.Apps.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (app == null) return false;

        _config.Apps.Remove(app);
        SaveConfig(_config);
        return true;
    }

    public bool UpdateApp(string name, AppEntry updatedApp)
    {
        var index = _config.Apps.FindIndex(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (index < 0) return false;

        _config.Apps[index] = updatedApp;
        SaveConfig(_config);
        return true;
    }

    // ============================
    // Controller Configuration
    // ============================

    public ControllerConfig GetControllerConfig()
    {
        return _config.Controller;
    }

    public void UpdateControllerConfig(ControllerConfig controllerConfig)
    {
        _config.Controller = controllerConfig;
        SaveConfig(_config);
        OnControllerConfigChanged?.Invoke(controllerConfig);
    }

    public void SetControllerButton(string buttonName, uint rawValue)
    {
        switch (buttonName.ToLower())
        {
            case "a": _config.Controller.ButtonA = rawValue; break;
            case "b": _config.Controller.ButtonB = rawValue; break;
            case "x": _config.Controller.ButtonX = rawValue; break;
            case "y": _config.Controller.ButtonY = rawValue; break;
            case "lb": _config.Controller.ButtonLB = rawValue; break;
            case "rb": _config.Controller.ButtonRB = rawValue; break;
            case "back": _config.Controller.ButtonBack = rawValue; break;
            case "start": _config.Controller.ButtonStart = rawValue; break;
            case "lstick": _config.Controller.ButtonLStick = rawValue; break;
            case "rstick": _config.Controller.ButtonRStick = rawValue; break;
        }
        SaveConfig(_config);
        OnControllerConfigChanged?.Invoke(_config.Controller);
    }

    // ============================
    // Utilities
    // ============================

    private string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    public string GetThumbnailFullPath(string relativePath)
    {
        var basePath = Path.GetDirectoryName(_configPath)!;
        return Path.Combine(basePath, relativePath);
    }

    public string ThumbnailsPath => _thumbnailsPath;
}
