namespace Kotak.Models;

public class WifiNetwork
{
    public string Ssid { get; set; } = string.Empty;
    public int SignalStrength { get; set; } // 0-100
    public bool IsSecured { get; set; }
    public bool IsConnected { get; set; }
    public string AuthType { get; set; } = string.Empty;
}
