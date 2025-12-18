using System.Text.Json.Serialization;

namespace Kotak.Models;

public class AppConfig
{
    [JsonPropertyName("apps")]
    public List<AppEntry> Apps { get; set; } = new();

    [JsonPropertyName("controller")]
    public ControllerConfig Controller { get; set; } = new();
}

public class AppEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "exe"; // "exe" or "web"

    [JsonPropertyName("path")]
    public string? Path { get; set; } // For EXE apps

    [JsonPropertyName("url")]
    public string? Url { get; set; } // For web apps

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; } // Optional launch arguments
}

public class ControllerConfig
{
    [JsonPropertyName("buttonA")]
    public uint ButtonA { get; set; } = 0x0002; // B2 - Select/Confirm

    [JsonPropertyName("buttonB")]
    public uint ButtonB { get; set; } = 0x0004; // B3 - Back/Cancel

    [JsonPropertyName("buttonX")]
    public uint ButtonX { get; set; } = 0x0001; // B1

    [JsonPropertyName("buttonY")]
    public uint ButtonY { get; set; } = 0x0008; // B4

    [JsonPropertyName("buttonLB")]
    public uint ButtonLB { get; set; } = 0x0010; // B5 - Left Bumper

    [JsonPropertyName("buttonRB")]
    public uint ButtonRB { get; set; } = 0x0020; // B6 - Right Bumper

    [JsonPropertyName("buttonBack")]
    public uint ButtonBack { get; set; } = 0x0040; // B7 - Back/Select

    [JsonPropertyName("buttonStart")]
    public uint ButtonStart { get; set; } = 0x0080; // B8 - Start

    [JsonPropertyName("buttonLStick")]
    public uint ButtonLStick { get; set; } = 0x0100; // B9 - Left Stick Click

    [JsonPropertyName("buttonRStick")]
    public uint ButtonRStick { get; set; } = 0x0200; // B10 - Right Stick Click

    // Helper method to get button name from raw value
    public static string GetButtonNameFromRaw(uint rawValue)
    {
        return rawValue switch
        {
            0x0001 => "B1",
            0x0002 => "B2",
            0x0004 => "B3",
            0x0008 => "B4",
            0x0010 => "B5",
            0x0020 => "B6",
            0x0040 => "B7",
            0x0080 => "B8",
            0x0100 => "B9",
            0x0200 => "B10",
            0x0400 => "B11",
            0x0800 => "B12",
            0x1000 => "B13",
            0x2000 => "B14",
            0x4000 => "B15",
            0x8000 => "B16",
            _ => $"0x{rawValue:X4}"
        };
    }
}
