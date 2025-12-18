using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using Kotak.Models;

namespace Kotak.Services;

public class IptvService
{
    private readonly HttpClient _httpClient;

    public IptvService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Kotak-IPTV/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Download and parse an M3U playlist from URL
    /// </summary>
    public async Task<IptvPlaylist?> ParsePlaylistAsync(string name, string url)
    {
        try
        {
            var content = await DownloadPlaylistAsync(url);
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            var channels = ParseM3uContent(content);

            return new IptvPlaylist
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Url = url,
                LastUpdated = DateTime.UtcNow,
                Channels = channels
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ParsePlaylistAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Refresh an existing playlist by re-fetching from URL
    /// </summary>
    public async Task<List<IptvChannel>?> RefreshPlaylistAsync(string url)
    {
        try
        {
            var content = await DownloadPlaylistAsync(url);
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            return ParseM3uContent(content);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshPlaylistAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download M3U content from URL
    /// </summary>
    private async Task<string?> DownloadPlaylistAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadPlaylistAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse M3U content into list of channels
    /// </summary>
    private List<IptvChannel> ParseM3uContent(string content)
    {
        var channels = new List<IptvChannel>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        IptvChannel? currentChannel = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines and M3U header
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#EXTM3U"))
            {
                continue;
            }

            // Parse EXTINF line (channel metadata)
            if (trimmedLine.StartsWith("#EXTINF:"))
            {
                currentChannel = ParseExtInfLine(trimmedLine);
            }
            // Stream URL line (follows EXTINF)
            else if (!trimmedLine.StartsWith("#") && currentChannel != null)
            {
                currentChannel.Url = trimmedLine;
                channels.Add(currentChannel);
                currentChannel = null;
            }
        }

        return channels;
    }

    /// <summary>
    /// Parse #EXTINF line to extract channel metadata
    /// Format: #EXTINF:-1 tvg-id="id" tvg-name="name" tvg-logo="logo" group-title="group",Channel Name
    /// </summary>
    private IptvChannel ParseExtInfLine(string line)
    {
        var channel = new IptvChannel
        {
            Id = Guid.NewGuid().ToString()
        };

        // Extract tvg-id
        var tvgIdMatch = Regex.Match(line, @"tvg-id=""([^""]*)""", RegexOptions.IgnoreCase);
        if (tvgIdMatch.Success)
        {
            channel.TvgId = tvgIdMatch.Groups[1].Value;
        }

        // Extract tvg-name
        var tvgNameMatch = Regex.Match(line, @"tvg-name=""([^""]*)""", RegexOptions.IgnoreCase);
        if (tvgNameMatch.Success)
        {
            channel.TvgName = tvgNameMatch.Groups[1].Value;
        }

        // Extract tvg-logo
        var logoMatch = Regex.Match(line, @"tvg-logo=""([^""]*)""", RegexOptions.IgnoreCase);
        if (logoMatch.Success)
        {
            channel.Logo = logoMatch.Groups[1].Value;
        }

        // Extract group-title
        var groupMatch = Regex.Match(line, @"group-title=""([^""]*)""", RegexOptions.IgnoreCase);
        if (groupMatch.Success)
        {
            channel.Group = groupMatch.Groups[1].Value;
        }

        // Extract channel name (text after the last comma)
        var commaIndex = line.LastIndexOf(',');
        if (commaIndex >= 0 && commaIndex < line.Length - 1)
        {
            channel.Name = line.Substring(commaIndex + 1).Trim();
        }
        else if (!string.IsNullOrEmpty(channel.TvgName))
        {
            // Fallback to tvg-name if no name after comma
            channel.Name = channel.TvgName;
        }
        else
        {
            channel.Name = "Unknown Channel";
        }

        return channel;
    }

    /// <summary>
    /// Validate if a URL looks like a valid stream URL
    /// </summary>
    public bool IsValidStreamUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Check for common stream URL patterns
        var validPrefixes = new[] { "http://", "https://", "rtmp://", "rtsp://" };
        return validPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validate if a URL looks like a valid M3U playlist URL
    /// </summary>
    public bool IsValidPlaylistUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check for common M3U extensions or patterns
        var lowerUrl = url.ToLowerInvariant();
        return lowerUrl.Contains(".m3u") ||
               lowerUrl.Contains("playlist") ||
               lowerUrl.Contains("iptv") ||
               lowerUrl.Contains("get.php") ||
               lowerUrl.Contains("type=m3u");
    }
}
