using System.IO.Compression;
using System.Text.Json;
using EmuSync.Domain.Objects;

namespace EmuSync.Domain.Helpers;

public static class ZipHelper
{
    private const string ManifestFileName = "manifest.json";

    public static void CreateCombinedZip(
        IReadOnlyList<GamePathEntry> children,
        string zipPath,
        Action<double>? onProgressChange = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        List<(int Index, string Root, string File)> files = [];
        HashSet<string> includedFiles = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        for (int index = 0; index < children.Count; index++)
        {
            GamePathEntry child = children[index];
            if (!child.Enabled || !Directory.Exists(child.Path)) continue;

            string root = Path.GetFullPath(child.Path);
            PathFilter filter = new(child.IncludeFilters, child.ExcludeFilters);
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                string relativePath = Path.GetRelativePath(root, fullPath);
                if (filter.Passes(relativePath) && includedFiles.Add(fullPath))
                {
                    files.Add((index, root, fullPath));
                }
            }
        }

        using FileStream fileStream = new(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(fileStream, ZipArchiveMode.Create);
        ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
        using (Stream manifestStream = manifestEntry.Open())
        {
            JsonSerializer.Serialize(manifestStream, new GameArchiveManifest { Version = 1, Children = children.ToList() });
        }

        for (int processed = 0; processed < files.Count; processed++)
        {
            (int index, string root, string file) = files[processed];
            string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            ZipArchiveEntry entry = archive.CreateEntry($"{index}/{relativePath}", CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using FileStream input = File.OpenRead(file);
            input.CopyTo(entryStream);
            onProgressChange?.Invoke((processed + 1) / (double)files.Count * 100);
        }

        if (files.Count == 0) onProgressChange?.Invoke(100);
    }

    public static void ExtractCombinedZip(
        Stream zipStream,
        IReadOnlyList<GamePathEntry> children,
        DateTime? forceLastWriteTime = null,
        Action<double>? onProgressChange = null)
    {
        zipStream.Position = 0;
        using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        bool combinedLayout = archive.GetEntry(ManifestFileName) != null;
        List<ZipArchiveEntry> entries = archive.Entries
            .Where(x => !string.IsNullOrEmpty(x.Name) && x.FullName != ManifestFileName)
            .ToList();

        for (int processed = 0; processed < entries.Count; processed++)
        {
            ZipArchiveEntry entry = entries[processed];
            (int childIndex, string relativePath) = ParseEntry(entry.FullName, combinedLayout);
            if (childIndex < 0 || childIndex >= children.Count) continue;

            GamePathEntry child = children[childIndex];
            if (!child.Enabled || !new PathFilter(child.IncludeFilters, child.ExcludeFilters).Passes(relativePath)) continue;

            string root = Path.GetFullPath(child.Path);
            string destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!destination.StartsWith(rootPrefix, comparison))
            {
                throw new InvalidDataException($"Zip entry '{entry.FullName}' escapes its configured directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            if (forceLastWriteTime.HasValue) File.SetLastWriteTimeUtc(destination, forceLastWriteTime.Value);
            onProgressChange?.Invoke((processed + 1) / (double)entries.Count * 100);
        }

        if (entries.Count == 0) onProgressChange?.Invoke(100);
    }

    private static (int ChildIndex, string RelativePath) ParseEntry(string entryName, bool combinedLayout)
    {
        string normalised = entryName.Replace('\\', '/');
        if (Path.IsPathRooted(normalised) || normalised.Split('/').Any(x => x == ".."))
        {
            throw new InvalidDataException($"Invalid zip entry '{entryName}'.");
        }

        if (!combinedLayout) return (0, normalised);

        int separator = normalised.IndexOf('/');
        if (separator <= 0 || !int.TryParse(normalised[..separator], out int childIndex))
        {
            throw new InvalidDataException($"Invalid combined zip entry '{entryName}'.");
        }

        return (childIndex, normalised[(separator + 1)..]);
    }

    /// <summary>
    /// Creates a zip of all files and folders found at <paramref name="folderPath"/>
    /// and writes it to <paramref name="zipPath"/>
    /// </summary>
    public static void CreateZipFromFolder(
        string folderPath,
        string zipPath,
        Action<double>? onProgressChange = null
    )
    {
        var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        int totalFiles = files.Length;
        int processedFiles = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using var fileStream = new FileStream(
            zipPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );

        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(folderPath, filePath);
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);

            using var entryStream = entry.Open();
            using var input = File.OpenRead(filePath);
            input.CopyTo(entryStream);

            processedFiles++;
            onProgressChange?.Invoke(
                totalFiles == 0 ? 100 : (processedFiles / (double)totalFiles) * 100
            );
        }
    }


    /// <summary>
    /// Extracts the in-memory zip to <paramref name="outputDirectory"/>
    /// </summary>
    /// <param name="zipStream"></param>
    /// <param name="outputDirectory"></param>
    /// <param name="forceLastWriteTime"></param>
    /// <param name="onProgressChange"></param>
    public static void ExtractToDirectory(
        Stream zipStream,
        string outputDirectory,
        DateTime? forceLastWriteTime = null,
        Action<double>? onProgressChange = null
    )
    {
        string? cleanOutputDirectory = GetOsSafePath(outputDirectory);
        if (string.IsNullOrEmpty(cleanOutputDirectory)) return;

        zipStream.Position = 0; //ensure start
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        if (Directory.Exists(cleanOutputDirectory))
        {
            Directory.Delete(cleanOutputDirectory, recursive: true);
        }

        Directory.CreateDirectory(cleanOutputDirectory);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        int totalEntries = entries.Count;
        int processedEntries = 0;

        foreach (var entry in entries)
        {
            var filePath = GetOsSafePath(
                Path.Combine(cleanOutputDirectory, entry.FullName)
            )!;

            //create directories if needed
            var directory = GetOsSafePath(
                Path.GetDirectoryName(filePath)
            );

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);

                if (forceLastWriteTime.HasValue)
                {
                    Directory.SetLastWriteTimeUtc(directory, forceLastWriteTime.Value);
                }
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            entry.ExtractToFile(filePath, overwrite: true);

            if (forceLastWriteTime.HasValue)
            {
                File.SetLastWriteTimeUtc(filePath, forceLastWriteTime.Value);
            }

            processedEntries++;
            onProgressChange?.Invoke((processedEntries / (double)totalEntries) * 100);
        }

        //stop false positives and ensure we keep the last write time on the local directory the same
        if (forceLastWriteTime.HasValue)
        {
            foreach (var dir in Directory.GetDirectories(cleanOutputDirectory, "*", SearchOption.AllDirectories))
            {
                Directory.SetLastWriteTimeUtc(dir, forceLastWriteTime.Value);
            }

            // finally, set the output directory itself
            Directory.SetLastWriteTimeUtc(cleanOutputDirectory, forceLastWriteTime.Value);
        }
    }

    private static string? GetOsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        bool isWindows = PlatformHelper.GetOsPlatform() == Enums.OsPlatform.Windows;

        if (isWindows)
        {
            return path.Replace("/", "\\");
        }

        return path.Replace("\\", "/");
    }

    private sealed record GameArchiveManifest
    {
        public int Version { get; init; }
        public List<GamePathEntry> Children { get; init; } = [];
    }
}
