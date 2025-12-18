using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Kotak.Models;

namespace Kotak.Services;

public class WifiService
{
    public List<WifiNetwork> ScanNetworks()
    {
        var networks = new List<WifiNetwork>();

        try
        {
            // Get available networks
            var output = RunNetsh("wlan show networks mode=bssid");
            networks = ParseNetworkList(output);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Wi-Fi scan failed: {ex.Message}");
        }

        return networks;
    }

    public string GetCurrentConnection()
    {
        try
        {
            var output = RunNetsh("wlan show interfaces");
            var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get current connection: {ex.Message}");
        }

        return string.Empty;
    }

    public bool Connect(string ssid, string password)
    {
        try
        {
            // Check if profile exists
            var profiles = RunNetsh("wlan show profiles");
            bool profileExists = profiles.Contains($"\"{ssid}\"") || profiles.Contains($": {ssid}");

            if (!profileExists && !string.IsNullOrEmpty(password))
            {
                // Create a temporary profile XML
                var profileXml = CreateWifiProfile(ssid, password);
                var tempFile = Path.Combine(Path.GetTempPath(), $"wifi_profile_{Guid.NewGuid()}.xml");

                try
                {
                    File.WriteAllText(tempFile, profileXml);
                    var addResult = RunNetsh($"wlan add profile filename=\"{tempFile}\"");
                    Debug.WriteLine($"Add profile result: {addResult}");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            }

            // Connect to the network
            var result = RunNetsh($"wlan connect name=\"{ssid}\"");
            return result.Contains("Connection request was completed successfully") ||
                   result.ToLower().Contains("successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Wi-Fi connect failed: {ex.Message}");
            return false;
        }
    }

    public bool Disconnect()
    {
        try
        {
            RunNetsh("wlan disconnect");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string RunNetsh(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(startInfo);
        if (process == null) return string.Empty;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);

        return output;
    }

    private List<WifiNetwork> ParseNetworkList(string output)
    {
        var networks = new List<WifiNetwork>();
        var currentSsid = GetCurrentConnection();

        // Split by SSID entries - match patterns like "SSID 1 : NetworkName"
        var lines = output.Split('\n');
        string? ssid = null;
        string authType = "";
        int signal = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Match SSID line (e.g., "SSID 1 : MyNetwork" or "SSID : MyNetwork")
            var ssidMatch = Regex.Match(trimmedLine, @"^SSID\s*\d*\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (ssidMatch.Success)
            {
                // Save previous network if exists
                if (!string.IsNullOrEmpty(ssid))
                {
                    networks.Add(new WifiNetwork
                    {
                        Ssid = ssid,
                        SignalStrength = signal,
                        AuthType = authType,
                        IsSecured = !authType.ToLower().Contains("open"),
                        IsConnected = ssid == currentSsid
                    });
                }

                ssid = ssidMatch.Groups[1].Value.Trim();
                authType = "";
                signal = 0;
                continue;
            }

            // Match Authentication
            var authMatch = Regex.Match(trimmedLine, @"^Authentication\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (authMatch.Success)
            {
                authType = authMatch.Groups[1].Value.Trim();
                continue;
            }

            // Match Signal
            var signalMatch = Regex.Match(trimmedLine, @"^Signal\s*:\s*(\d+)%$", RegexOptions.IgnoreCase);
            if (signalMatch.Success)
            {
                signal = int.Parse(signalMatch.Groups[1].Value);
                continue;
            }
        }

        // Add last network
        if (!string.IsNullOrEmpty(ssid))
        {
            networks.Add(new WifiNetwork
            {
                Ssid = ssid,
                SignalStrength = signal,
                AuthType = authType,
                IsSecured = !authType.ToLower().Contains("open"),
                IsConnected = ssid == currentSsid
            });
        }

        // Remove duplicates and sort by signal strength
        return networks
            .GroupBy(n => n.Ssid)
            .Select(g => g.OrderByDescending(n => n.SignalStrength).First())
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.SignalStrength)
            .ToList();
    }

    private string CreateWifiProfile(string ssid, string password)
    {
        var escapedSsid = EscapeXml(ssid);
        var escapedPassword = EscapeXml(password);

        // WPA2-Personal profile XML
        return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{escapedSsid}</name>
    <SSIDConfig>
        <SSID>
            <name>{escapedSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{escapedPassword}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
    }

    private static string EscapeXml(string str)
    {
        return str
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
