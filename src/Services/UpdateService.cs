using System.Diagnostics;
using System.IO;
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
