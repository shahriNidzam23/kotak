using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Kotak.Models;

namespace Kotak.Services;

public class AppLauncherService
{
    public void Launch(AppEntry app)
    {
        try
        {
            if (app.Type.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                LaunchWebApp(app);
            }
            else
            {
                LaunchExeApp(app);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch {app.Name}: {ex.Message}");
            throw;
        }
    }

    private void LaunchWebApp(AppEntry app)
    {
        if (string.IsNullOrEmpty(app.Url)) return;

        // Open in default browser
        var startInfo = new ProcessStartInfo
        {
            FileName = app.Url,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private void LaunchExeApp(AppEntry app)
    {
        if (string.IsNullOrEmpty(app.Path))
        {
            throw new ArgumentException($"Application path is empty for: {app.Name}");
        }

        if (!File.Exists(app.Path))
        {
            throw new FileNotFoundException($"Application not found: {app.Path}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = app.Path,
            Arguments = app.Arguments ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(app.Path),
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    /// <summary>
    /// Extract icon from EXE and save as PNG thumbnail
    /// </summary>
    public string? ExtractIconFromExe(string exePath, string thumbnailsFolder, string appName)
    {
        try
        {
            if (!File.Exists(exePath)) return null;

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            // Sanitize app name for filename
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = string.Join("_", appName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            var thumbnailPath = Path.Combine(thumbnailsFolder, $"{safeName}.png");

            // Convert icon to bitmap and save as PNG
            using var bitmap = icon.ToBitmap();

            // Create a higher quality scaled version (128x128)
            using var scaledBitmap = new Bitmap(128, 128);
            using (var graphics = Graphics.FromImage(scaledBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(bitmap, 0, 0, 128, 128);
            }

            scaledBitmap.Save(thumbnailPath, ImageFormat.Png);

            return thumbnailPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to extract icon from {exePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get app name from executable path
    /// </summary>
    public string GetAppNameFromPath(string exePath)
    {
        try
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);

            // Try to get product name first
            if (!string.IsNullOrWhiteSpace(fileVersionInfo.ProductName))
            {
                return fileVersionInfo.ProductName.Trim();
            }

            // Fall back to file description
            if (!string.IsNullOrWhiteSpace(fileVersionInfo.FileDescription))
            {
                return fileVersionInfo.FileDescription.Trim();
            }

            // Fall back to filename without extension
            return Path.GetFileNameWithoutExtension(exePath);
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(exePath);
        }
    }
}
