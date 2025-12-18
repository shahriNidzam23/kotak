using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Kotak.Services;

public class SystemService
{
    // ============================
    // Volume Control via COM
    // ============================

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int NotImpl1();
        int NotImpl2();
        int GetChannelCount(out int channelCount);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int NotImpl3();
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
    }

    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

    public int GetVolume()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
            device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out object o);
            var volume = (IAudioEndpointVolume)o;
            volume.GetMasterVolumeLevelScalar(out float level);
            return (int)(level * 100);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetVolume error: {ex.Message}");
            return 50; // Default
        }
    }

    public void SetVolume(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
            device.Activate(IID_IAudioEndpointVolume, 1, IntPtr.Zero, out object o);
            var volume = (IAudioEndpointVolume)o;
            var guid = Guid.Empty;
            volume.SetMasterVolumeLevelScalar(level / 100f, ref guid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetVolume error: {ex.Message}");
        }
    }

    // ============================
    // Brightness Control via WMI
    // ============================

    public int GetBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                return Convert.ToInt32(obj["CurrentBrightness"]);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetBrightness error: {ex.Message}");
        }
        return 100; // Default for desktops or if WMI fails
    }

    public bool SetBrightness(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { 1, level });
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetBrightness error: {ex.Message}");
        }
        return false; // Brightness control not available (desktop monitor)
    }

    public bool IsBrightnessSupported()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
            return searcher.Get().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    // ============================
    // Power Management
    // ============================

    public void Shutdown()
    {
        ExecuteShutdownCommand("/s /t 0");
    }

    public void Restart()
    {
        ExecuteShutdownCommand("/r /t 0");
    }

    public void Sleep()
    {
        // Put system to sleep
        SetSuspendState(false, false, false);
    }

    public void Hibernate()
    {
        // Put system to hibernate
        SetSuspendState(true, false, false);
    }

    private void ExecuteShutdownCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(startInfo);
    }

    // P/Invoke for sleep/hibernate
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    // ============================
    // Desktop & Browser
    // ============================

    /// <summary>
    /// Show desktop by minimizing all windows (Win+D equivalent)
    /// </summary>
    public void ShowDesktop()
    {
        try
        {
            // Simulate Win+D keypress to toggle desktop view
            keybd_event(VK_LWIN, 0, 0, 0);
            keybd_event(VK_D, 0, 0, 0);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowDesktop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Open Microsoft Edge browser
    /// </summary>
    public void OpenBrowser()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "msedge",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenBrowser (Edge) error: {ex.Message}");
            // Fallback to default browser
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "https://www.bing.com",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"OpenBrowser (fallback) error: {ex2.Message}");
            }
        }
    }

    // P/Invoke for keyboard simulation (fallback for ShowDesktop)
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const byte VK_LWIN = 0x5B;
    private const byte VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // ============================
    // Tailscale VPN
    // ============================

    /// <summary>
    /// Start Tailscale VPN if not already running
    /// </summary>
    public (bool success, string message) StartTailscale()
    {
        try
        {
            // First check if Tailscale is already running
            var statusInfo = new ProcessStartInfo
            {
                FileName = "tailscale",
                Arguments = "status",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var statusProcess = Process.Start(statusInfo);
            var output = statusProcess?.StandardOutput.ReadToEnd() ?? "";
            statusProcess?.WaitForExit();

            // Check if already connected (output contains @ which indicates logged in user)
            if (output.Contains("@") && !output.Contains("Tailscale is stopped"))
            {
                return (true, "Tailscale is already running");
            }

            // Try to start Tailscale
            var upInfo = new ProcessStartInfo
            {
                FileName = "tailscale",
                Arguments = "up",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var upProcess = Process.Start(upInfo);
            upProcess?.WaitForExit(10000); // 10 second timeout

            if (upProcess?.ExitCode == 0)
            {
                return (true, "Tailscale started successfully");
            }
            else
            {
                return (false, "Failed to start Tailscale. Please check if it's installed and you're logged in.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartTailscale error: {ex.Message}");
            return (false, "Tailscale not found. Please install Tailscale first.");
        }
    }
}
