using EmuSync.Domain.Enums;
using EmuSync.Domain.Helpers;
using EmuSync.Domain.Objects;
using EmuSync.Domain.Results;
using EmuSync.Domain.Services.Interfaces;
using System.Text.Json;

namespace EmuSync.Domain.Services;

public class LocalDataAccessor : ILocalDataAccessor
{
    public async Task<T> ReadFileContentsAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        await using var fs = File.OpenRead(filePath);
        return (await JsonSerializer.DeserializeAsync<T>(fs, cancellationToken: cancellationToken))!;
    }

    public async Task<T?> ReadFileContentsOrDefaultAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return default;

        return await ReadFileContentsAsync<T>(filePath, cancellationToken);
    }

    public async Task WriteFileContentsAsync(string filePath, object data, CancellationToken cancellationToken = default)
    {
        CreateFolderIfNotExists(filePath);

        var options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        await using var fs = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true
        );

        await JsonSerializer.SerializeAsync(fs, data, options, cancellationToken);
    }

    public void RemoveFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        File.Delete(filePath);
    }

    public string GetLocalFilePath(string fileName)
    {
        string localFolder = GetLocalFolderPath();
        return Path.Combine(localFolder, fileName);
    }

    public DirectoryScanResult ScanDirectory(string? path)
    {
        return ScanDirectory(path, null);
    }

    public DirectoryScanResult ScanDirectories(IEnumerable<GamePathEntry> paths)
    {
        List<DirectoryScanResult> scans = paths
            .Where(x => x.Enabled)
            .Select(x => ScanDirectory(x.Path, new PathFilter(x.IncludeFilters, x.ExcludeFilters)))
            .ToList();

        return new DirectoryScanResult
        {
            DirectoryIsSet = scans.Count > 0,
            DirectoryExists = scans.Count > 0 && scans.All(x => x.DirectoryExists),
            FileCount = scans.Sum(x => x.FileCount),
            DirectoryCount = scans.Sum(x => x.DirectoryCount),
            StorageBytes = scans.Sum(x => x.StorageBytes),
            LatestFileWriteTimeUtc = Max(scans.Select(x => x.LatestFileWriteTimeUtc)),
            LatestDirectoryWriteTimeUtc = Max(scans.Select(x => x.LatestDirectoryWriteTimeUtc)),
            ScannedDirectories = scans
        };
    }

    private DirectoryScanResult ScanDirectory(string? path, PathFilter? filter)
    {
        DirectoryScanResult result = new() { Path = path };

        if (string.IsNullOrEmpty(path))
        {
            result.DirectoryIsSet = false;
            return result;
        }

        result.DirectoryIsSet = true;
        result.DirectoryExists = Directory.Exists(path);

        if (!result.DirectoryExists) return result;

        DirectoryInfo directoryInfo = new DirectoryInfo(path);
        result.LatestDirectoryWriteTimeUtc = directoryInfo.LastWriteTimeUtc;

        SearchDirectory(result, path, path, filter);

        return result;
    }

    private void SearchDirectory(DirectoryScanResult scanResult, string rootPath, string path, PathFilter? filter)
    {
        var files = Directory.EnumerateFiles(path);

        if (files != null)
        {
            foreach (string file in files)
            {
                if (filter != null && !filter.Passes(Path.GetRelativePath(rootPath, file))) continue;

                scanResult.FileCount++;

                FileInfo fileInfo = new FileInfo(file);

                scanResult.StorageBytes += fileInfo.Length;

                DateTime latest = scanResult.LatestDirectoryWriteTimeUtc ?? DateTime.MinValue;
                if (fileInfo.LastWriteTimeUtc > latest)
                {
                    scanResult.LatestFileWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                }
            }
        }

        var directories = Directory.EnumerateDirectories(path);

        if (directories != null)
        {
            foreach (string directory in directories)
            {
                scanResult.DirectoryCount++;

                DirectoryInfo directoryInfo = new DirectoryInfo(directory);

                DateTime latest = scanResult.LatestFileWriteTimeUtc ?? DateTime.MinValue;
                if (directoryInfo.LastWriteTimeUtc > latest)
                {
                    scanResult.LatestDirectoryWriteTimeUtc = directoryInfo.LastWriteTimeUtc;
                }

                SearchDirectory(scanResult, rootPath, directory, filter);
            }
        }
    }

    private static DateTime? Max(IEnumerable<DateTime?> values)
    {
        DateTime[] setValues = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return setValues.Length == 0 ? null : setValues.Max();
    }

    /// <summary>
    /// Gets the <see cref="DomainConstants.LocalDataFolder"/> in the current user profile
    /// </summary>
    /// <returns></returns>
    private string GetLocalFolderPath()
    {
        OsPlatform platform = PlatformHelper.GetOsPlatform();

        string folderPath = platform == OsPlatform.Windows
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(folderPath, DomainConstants.LocalDataFolder);
    }

    /// <summary>
    /// Creates the path if it doesn't exist
    /// </summary>
    /// <param name="filePath"></param>
    private void CreateFolderIfNotExists(string filePath)
    {
        var containingFolder = Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(containingFolder)) return;
        if (Directory.Exists(containingFolder)) return;

        Directory.CreateDirectory(containingFolder);
    }
}
