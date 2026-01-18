using System.IO.Compression;

namespace osync;

/// <summary>
/// Helper class for creating backup archives of results files
/// Uses Zip format with optimal compression for JSON files
/// </summary>
public static class BackupHelper
{
    /// <summary>
    /// Creates a compressed backup of the specified file, overwriting any existing backup
    /// </summary>
    /// <param name="sourceFilePath">Path to the file to backup</param>
    /// <param name="log">Optional logging action for status messages</param>
    /// <returns>True if backup was successful, false otherwise</returns>
    public static bool CreateBackup(string sourceFilePath, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            return false;
        }

        var backupPath = sourceFilePath + ".backup.zip";

        try
        {
            // Delete existing backup if present
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            // Create zip archive with the source file using optimal compression
            using (var zipStream = new FileStream(backupPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var fileName = Path.GetFileName(sourceFilePath);
                var entry = archive.CreateEntry(fileName, CompressionLevel.SmallestSize);

                using var entryStream = entry.Open();
                using var sourceStream = File.OpenRead(sourceFilePath);
                sourceStream.CopyTo(entryStream);
            }

            var backupInfo = new FileInfo(backupPath);
            var sourceInfo = new FileInfo(sourceFilePath);
            var ratio = (double)backupInfo.Length / sourceInfo.Length * 100;
            log?.Invoke($"[dim]Backup created: {Path.GetFileName(backupPath)} ({ratio:F0}% of original)[/]");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[yellow]Warning: Could not create backup: {ex.Message}[/]");

            // Clean up partial backup file
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch { }

            return false;
        }
    }

    /// <summary>
    /// Restores a file from its backup
    /// </summary>
    /// <param name="originalFilePath">Path to the original file (backup path is derived from this)</param>
    /// <param name="log">Optional logging action for status messages</param>
    /// <returns>True if restore was successful, false otherwise</returns>
    public static bool RestoreBackup(string originalFilePath, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(originalFilePath))
        {
            return false;
        }

        var backupPath = originalFilePath + ".backup.zip";

        if (!File.Exists(backupPath))
        {
            log?.Invoke($"[yellow]No backup found at: {backupPath}[/]");
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            var entry = archive.Entries.FirstOrDefault();

            if (entry == null)
            {
                log?.Invoke($"[red]Backup archive is empty[/]");
                return false;
            }

            // Extract to a temp file first
            var tempPath = originalFilePath + ".restore.tmp";
            entry.ExtractToFile(tempPath, overwrite: true);

            // Replace original with restored file
            if (File.Exists(originalFilePath))
            {
                File.Delete(originalFilePath);
            }
            File.Move(tempPath, originalFilePath);

            log?.Invoke($"[green]Restored from backup: {Path.GetFileName(backupPath)}[/]");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[red]Error restoring backup: {ex.Message}[/]");
            return false;
        }
    }

    /// <summary>
    /// Checks if a backup exists for the specified file
    /// </summary>
    public static bool BackupExists(string sourceFilePath)
    {
        if (string.IsNullOrEmpty(sourceFilePath))
        {
            return false;
        }

        var backupPath = sourceFilePath + ".backup.zip";
        return File.Exists(backupPath);
    }

    /// <summary>
    /// Gets the backup file path for a given source file
    /// </summary>
    public static string GetBackupPath(string sourceFilePath)
    {
        return sourceFilePath + ".backup.zip";
    }
}
