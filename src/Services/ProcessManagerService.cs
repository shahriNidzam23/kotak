using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Kotak.Models;

namespace Kotak.Services;

public class ProcessManagerService
{
    // P/Invoke for window management
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    /// <summary>
    /// Get running processes that match registered apps
    /// </summary>
    public List<RunningAppInfo> GetRunningApps(List<AppEntry> registeredApps)
    {
        var runningApps = new List<RunningAppInfo>();

        try
        {
            var processes = Process.GetProcesses();

            foreach (var app in registeredApps)
            {
                // Skip web apps
                if (string.IsNullOrEmpty(app.Path) || app.Type.Equals("web", StringComparison.OrdinalIgnoreCase))
                    continue;

                var exeName = Path.GetFileNameWithoutExtension(app.Path);

                // Find matching processes
                var matchingProcesses = processes
                    .Where(p =>
                    {
                        try
                        {
                            return p.ProcessName.Equals(exeName, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToList();

                if (matchingProcesses.Any())
                {
                    var process = matchingProcesses.First();
                    runningApps.Add(new RunningAppInfo
                    {
                        AppName = app.Name,
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        Thumbnail = app.Thumbnail ?? string.Empty,
                        MainWindowHandle = process.MainWindowHandle.ToInt64(),
                        HasWindow = process.MainWindowHandle != IntPtr.Zero
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting running apps: {ex.Message}");
        }

        return runningApps;
    }

    /// <summary>
    /// Kill a process by its ID
    /// </summary>
    public bool KillProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(3000); // Wait up to 3 seconds
            return true;
        }
        catch (ArgumentException)
        {
            // Process already exited
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error killing process {processId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Bring a process window to foreground
    /// </summary>
    public bool FocusProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var handle = process.MainWindowHandle;

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            // Restore if minimized
            if (IsIconic(handle))
            {
                ShowWindow(handle, SW_RESTORE);
            }

            // Bring to foreground
            return SetForegroundWindow(handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error focusing process {processId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a process is still running
    /// </summary>
    public bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

public class RunningAppInfo
{
    public string AppName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public long MainWindowHandle { get; set; }
    public bool HasWindow { get; set; }
}
