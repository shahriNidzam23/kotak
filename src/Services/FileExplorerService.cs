using System.Diagnostics;
using System.IO;

namespace Kotak.Services;

public class FileExplorerService
{
    /// <summary>
    /// Get list of available drives
    /// </summary>
    public List<DriveEntry> GetDrives()
    {
        var drives = new List<DriveEntry>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    drives.Add(new DriveEntry
                    {
                        Name = drive.Name,
                        Label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel,
                        Type = drive.DriveType.ToString(),
                        TotalSize = drive.TotalSize,
                        FreeSpace = drive.TotalFreeSpace
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting drives: {ex.Message}");
        }

        return drives;
    }

    /// <summary>
    /// Get contents of a directory
    /// </summary>
    public DirectoryContents GetDirectoryContents(string path)
    {
        var result = new DirectoryContents
        {
            CurrentPath = path,
            Items = new List<FileSystemEntry>()
        };

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                result.Error = "Directory does not exist";
                return result;
            }

            result.ParentPath = directoryInfo.Parent?.FullName;

            // Get directories first
            try
            {
                foreach (var dir in directoryInfo.GetDirectories())
                {
                    // Skip hidden and system directories
                    if ((dir.Attributes & FileAttributes.Hidden) != 0 ||
                        (dir.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    result.Items.Add(new FileSystemEntry
                    {
                        Name = dir.Name,
                        Path = dir.FullName,
                        IsDirectory = true,
                        LastModified = dir.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }

            // Then get files
            try
            {
                foreach (var file in directoryInfo.GetFiles())
                {
                    // Skip hidden files
                    if ((file.Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }

                    result.Items.Add(new FileSystemEntry
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension.ToLowerInvariant()
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we can't access
            }

            // Sort: directories first, then alphabetically
            result.Items = result.Items
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            result.Error = "Access denied";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Debug.WriteLine($"Error reading directory {path}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get user's home directory (Documents, Downloads, etc.)
    /// </summary>
    public string GetUserHome()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Get Downloads folder
    /// </summary>
    public string GetDownloadsFolder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>
    /// Format file size to human-readable string
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

public class DriveEntry
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
}

public class DirectoryContents
{
    public string CurrentPath { get; set; } = string.Empty;
    public string? ParentPath { get; set; }
    public List<FileSystemEntry> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class FileSystemEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}
