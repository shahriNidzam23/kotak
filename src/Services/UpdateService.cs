using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Kotak.Services;

public class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/shahriNidzam23/kotak/releases/latest";
    private const string GitHubReleasesUrl = "https://github.com/shahriNidzam23/kotak/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Kotak-Updater");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    /// <summary>
    /// Get the current application version
    /// </summary>
    public string GetCurrentVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    /// <summary>
    /// Check for updates from GitHub releases
    /// </summary>
    public async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(GitHubApiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release == null)
            {
                return new UpdateInfo { HasUpdate = false, Error = "Failed to parse release info" };
            }

            var latestVersion = release.TagName?.TrimStart('v', 'V') ?? "0.0.0";
            var currentVersion = GetCurrentVersion();

            var hasUpdate = IsNewerVersion(latestVersion, currentVersion);

            // Find the exe download URL
            string? downloadUrl = null;
            string? fileName = null;
            long fileSize = 0;

            if (release.Assets != null)
            {
                foreach (var asset in release.Assets)
                {
                    if (asset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ||
                        asset.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        fileName = asset.Name;
                        fileSize = asset.Size;
                        break;
                    }
                }
            }

            return new UpdateInfo
            {
                HasUpdate = hasUpdate,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseNotes = release.Body ?? "",
                ReleaseName = release.Name ?? $"v{latestVersion}",
                ReleaseUrl = release.HtmlUrl ?? GitHubReleasesUrl,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                FileSize = fileSize,
                PublishedAt = release.PublishedAt
            };
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"CheckForUpdates HTTP error: {ex.Message}");
            return new UpdateInfo { HasUpdate = false, Error = "Network error. Check your internet connection." };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CheckForUpdates error: {ex.Message}");
            return new UpdateInfo { HasUpdate = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Open the releases page in default browser
    /// </summary>
    public void OpenReleasesPage()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GitHubReleasesUrl,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenReleasesPage error: {ex.Message}");
        }
    }

    /// <summary>
    /// Download update to temp folder and return the path
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(string downloadUrl, string fileName, Action<int>? onProgress = null)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "KotakUpdate");
            Directory.CreateDirectory(tempPath);
            var filePath = Path.Combine(tempPath, fileName);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            var bytesRead = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            int read;
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    var progress = (int)((bytesRead * 100) / totalBytes);
                    onProgress?.Invoke(progress);
                }
            }

            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadUpdate error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extract the downloaded zip to a temp folder
    /// </summary>
    public async Task<string?> ExtractUpdateAsync(string zipPath, Action<string>? onStatus = null)
    {
        try
        {
            var extractPath = Path.Combine(Path.GetTempPath(), "KotakUpdate", "extracted");

            // Clean up existing extraction folder
            if (Directory.Exists(extractPath))
            {
                onStatus?.Invoke("Cleaning up previous extraction...");
                Directory.Delete(extractPath, true);
            }

            Directory.CreateDirectory(extractPath);

            onStatus?.Invoke("Extracting update files...");
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath));

            return extractPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExtractUpdate error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate the batch script for update installation
    /// </summary>
    public string? GenerateUpdateBatchScript(string extractedPath)
    {
        try
        {
            var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var kotakExe = Environment.ProcessPath ?? Path.Combine(appDir, "Kotak.exe");
            var batchPath = Path.Combine(Path.GetTempPath(), "KotakUpdate", "update_kotak.bat");

            var batchContent = $@"@echo off
setlocal enabledelayedexpansion
title KOTAK Updater

echo ========================================
echo KOTAK Auto-Updater
echo ========================================
echo.

REM Wait for Kotak.exe to close (with timeout)
echo Waiting for KOTAK to close...
set /a TIMEOUT_COUNT=0
:wait_loop
tasklist /FI ""IMAGENAME eq Kotak.exe"" 2>NUL | find /I /N ""Kotak.exe"">NUL
if ""%ERRORLEVEL%""==""0"" (
    set /a TIMEOUT_COUNT+=1
    if !TIMEOUT_COUNT! GEQ 30 (
        echo ERROR: Timeout waiting for KOTAK to close.
        echo Please close KOTAK manually and try again.
        pause
        exit /b 1
    )
    timeout /t 1 /nobreak >nul
    goto wait_loop
)
echo KOTAK closed.
echo.

REM Create backup folder with timestamp
for /f ""tokens=2 delims=="" %%a in ('wmic OS Get localdatetime /value') do set ""dt=%%a""
set ""BACKUP_DIR={appDir}\backup_!dt:~0,8!_!dt:~8,6!""
echo Creating backup at: !BACKUP_DIR!
mkdir ""!BACKUP_DIR!"" 2>nul

REM Backup existing files
echo Backing up existing files...
if exist ""{appDir}\Kotak.exe"" copy /Y ""{appDir}\Kotak.exe"" ""!BACKUP_DIR!\Kotak.exe"" >nul
if exist ""{appDir}\WebUI"" xcopy /E /I /Q /Y ""{appDir}\WebUI"" ""!BACKUP_DIR!\WebUI"" >nul

REM Copy new files from extracted update
echo.
echo Installing update...

REM Copy new Kotak.exe
if exist ""{extractedPath}\Kotak.exe"" (
    copy /Y ""{extractedPath}\Kotak.exe"" ""{appDir}\Kotak.exe""
    if !ERRORLEVEL! NEQ 0 (
        echo ERROR: Failed to copy Kotak.exe
        echo Restoring backup...
        copy /Y ""!BACKUP_DIR!\Kotak.exe"" ""{appDir}\Kotak.exe""
        pause
        exit /b 1
    )
    echo   Updated: Kotak.exe
)

REM Copy WebUI folder
if exist ""{extractedPath}\WebUI"" (
    xcopy /E /I /Q /Y ""{extractedPath}\WebUI"" ""{appDir}\WebUI""
    if !ERRORLEVEL! NEQ 0 (
        echo ERROR: Failed to copy WebUI
        echo Restoring backup...
        xcopy /E /I /Q /Y ""!BACKUP_DIR!\WebUI"" ""{appDir}\WebUI""
        pause
        exit /b 1
    )
    echo   Updated: WebUI folder
)

REM Copy PDB files (if exist)
for %%f in (""{extractedPath}\*.pdb"") do (
    copy /Y ""%%f"" ""{appDir}\"" >nul
    echo   Updated: %%~nxf
)

REM DO NOT copy config.json or thumbnails (preserve user data)
echo.
echo Preserved user data: config.json, thumbnails/

REM Cleanup temp files
echo.
echo Cleaning up temporary files...
rd /S /Q ""{Path.Combine(Path.GetTempPath(), "KotakUpdate")}"" 2>nul

REM Restart KOTAK
echo.
echo ========================================
echo Update complete! Starting KOTAK...
echo ========================================
timeout /t 2 /nobreak >nul
start """" ""{kotakExe}""

exit
";

            File.WriteAllText(batchPath, batchContent);
            return batchPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GenerateUpdateBatchScript error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Launch the update batch script
    /// </summary>
    public bool LaunchUpdateScript(string batchPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LaunchUpdateScript error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Compare semantic versions
    /// </summary>
    private bool IsNewerVersion(string latest, string current)
    {
        try
        {
            var latestParts = latest.Split('.').Select(int.Parse).ToArray();
            var currentParts = current.Split('.').Select(int.Parse).ToArray();

            // Ensure both have 3 parts
            while (latestParts.Length < 3) latestParts = latestParts.Concat(new[] { 0 }).ToArray();
            while (currentParts.Length < 3) currentParts = currentParts.Concat(new[] { 0 }).ToArray();

            // Compare major.minor.patch
            for (int i = 0; i < 3; i++)
            {
                if (latestParts[i] > currentParts[i]) return true;
                if (latestParts[i] < currentParts[i]) return false;
            }

            return false; // Same version
        }
        catch
        {
            return false;
        }
    }
}

public class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? ReleaseName { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? DownloadUrl { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? PublishedAt { get; set; }
    public string? Error { get; set; }
}

public class GitHubRelease
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public string? HtmlUrl { get; set; }
    public string? PublishedAt { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    public string? Name { get; set; }
    public string? BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
}
