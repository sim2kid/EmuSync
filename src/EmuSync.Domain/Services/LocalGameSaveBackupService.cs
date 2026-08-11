using EmuSync.Domain.Entities;
using EmuSync.Domain.Helpers;
using EmuSync.Domain.Objects;
using EmuSync.Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmuSync.Domain.Services;


public class LocalGameSaveBackupService(
    ILogger<LocalGameSaveBackupService> logger,
    ILocalDataAccessor localDataAccessor
) : ILocalGameSaveBackupService
{
    private readonly ILogger<LocalGameSaveBackupService> _logger = logger;
    private readonly ILocalDataAccessor _localDataAccessor = localDataAccessor;

    public async Task<List<LocalGameBackupManifestEntity>> GetBackupsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        List<LocalGameBackupManifestEntity>? manifests = await GetBackupManifestAsync(gameId, cancellationToken);
        return manifests ?? [];
    }

    public async Task CreateBackupAsync(GameEntity game, IReadOnlyList<GamePathEntry> paths, Action<double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        string fileName = string.Format(DomainConstants.LocalDataGameBackupFileNameFormat, now.ToString("dd-MM-yyyy_HH-mm-ss"));
        string fullBackupLocation = GetGameBackupFileName(game.Id, fileName);

        if (!paths.Any(x => x.Enabled && Directory.Exists(x.Path)))
        {
            _logger.LogInformation("No local locations existed, skipping backup for game {gameId}", game.Id);
            return;
        }

        //create the zip backup content
        ZipHelper.CreateCombinedZip(paths, fullBackupLocation, onProgress);
        await AddBackupToManifestAsync(game, fileName, now, cancellationToken);
    }

    public async Task RestoreBackupAsync(string gameId, string backupId, IReadOnlyList<GamePathEntry> paths, CancellationToken cancellationToken = default)
    {
        List<LocalGameBackupManifestEntity>? manifests = await GetBackupManifestAsync(gameId, cancellationToken);
        LocalGameBackupManifestEntity? manifest = manifests?.FirstOrDefault(x => x.Id == backupId);

        if (manifest == null)
        {
            _logger.LogError("No backup manifest found for game {gameId}, backup ID {backupId}", gameId, backupId);
            throw new InvalidOperationException($"No backup manifest found for game {gameId}, backup ID {backupId}");
        }

        string fullBackupLocation = GetGameBackupFileName(gameId, manifest.BackupFileName);

        if (!File.Exists(fullBackupLocation))
        {
            _logger.LogError("Backup file didn't exist at {fileLocation}", fullBackupLocation);
            throw new FileNotFoundException("Failed to find backup file", fullBackupLocation);
        }

        using var fileStream = new FileStream(fullBackupLocation, FileMode.Open, FileAccess.Read);
        ZipHelper.ExtractCombinedZip(fileStream, paths, DateTime.UtcNow);
    }

    public async Task DeleteBackupAsync(string gameId, string backupId, CancellationToken cancellationToken = default)
    {
        List<LocalGameBackupManifestEntity>? manifests = await GetBackupManifestAsync(gameId, cancellationToken);
        LocalGameBackupManifestEntity? manifest = manifests?.FirstOrDefault(x => x.Id == backupId);

        if (manifest == null)
        {
            _logger.LogError("No backup manifest found for game {gameId}, backup ID {backupId}", gameId, backupId);
            throw new InvalidOperationException($"No backup manifest found for game {gameId}, backup ID {backupId}");
        }

        string fullBackupLocation = GetGameBackupFileName(gameId, manifest.BackupFileName);

        if (!File.Exists(fullBackupLocation))
        {
            _logger.LogError("Backup file didn't exist at {fileLocation}", fullBackupLocation);
            throw new FileNotFoundException("Failed to find backup file", fullBackupLocation);
        }

        File.Delete(fullBackupLocation);

        manifests!.Remove(manifest);
        string manifestFile = GetGameBackupManifestFileName(gameId);
        await _localDataAccessor.WriteFileContentsAsync(manifestFile, manifests, cancellationToken);
    }

    private async Task AddBackupToManifestAsync(GameEntity game, string fileName, DateTime createdOnUtc, CancellationToken cancellationToken = default)
    {
        List<LocalGameBackupManifestEntity>? manifests = await GetBackupManifestAsync(game.Id, cancellationToken);
        manifests ??= [];

        LocalGameBackupManifestEntity manifest = new()
        {
            Id = IdHelper.Create(),
            BackupFileName = fileName,
            GameId = game.Id,
            CreatedOnUtc = createdOnUtc
        };

        manifests.Add(manifest);

        //new backup created - now we need to trim the amount of backups to the limit we have set
        SyncSourceEntity syncSource = await GetLocalSyncSourceAsync(cancellationToken);

        int maxBackups = game.MaximumLocalGameBackups
            ?? syncSource.MaximumLocalGameBackups
            ?? DomainConstants.DefaultMaximumLocalGameBackups;

        if (manifests.Count > maxBackups)
        {
            DeleteOldBackups(game.Id, manifests, maxBackups);
        }

        string manifestFile = GetGameBackupManifestFileName(game.Id);
        await _localDataAccessor.WriteFileContentsAsync(manifestFile, manifests, cancellationToken);
    }

    private void DeleteOldBackups(string gameId, List<LocalGameBackupManifestEntity> manifests, int maxBackups)
    {
        List<LocalGameBackupManifestEntity> backupsToDelete = manifests
                        .OrderBy(m => m.CreatedOnUtc)
                        .Take(manifests.Count - maxBackups)
                        .ToList();

        foreach (var backup in backupsToDelete)
        {
            string backupFile = GetGameBackupFileName(gameId, backup.BackupFileName);
            _localDataAccessor.RemoveFile(backupFile);

            manifests.Remove(backup);
        }
    }

    private async Task<List<LocalGameBackupManifestEntity>?> GetBackupManifestAsync(string gameId, CancellationToken cancellationToken = default)
    {
        string manifestFile = GetGameBackupManifestFileName(gameId);
        return await _localDataAccessor.ReadFileContentsOrDefaultAsync<List<LocalGameBackupManifestEntity>>(manifestFile, cancellationToken);
    }

    private string GetGameBackupManifestFileName(string gameId)
    {
        string fileName = Path.Combine(DomainConstants.LocalDataGameBackupFolder, gameId, DomainConstants.LocalDataGameBackupManifestFile);
        return _localDataAccessor.GetLocalFilePath(fileName);
    }

    private string GetGameBackupFileName(string gameId, string fileName)
    {
        string gameBackupFile = Path.Combine(DomainConstants.LocalDataGameBackupFolder, gameId, fileName);
        return _localDataAccessor.GetLocalFilePath(gameBackupFile);
    }

    private async Task<SyncSourceEntity> GetLocalSyncSourceAsync(CancellationToken cancellationToken = default)
    {
        string filePath = _localDataAccessor.GetLocalFilePath(DomainConstants.LocalDataSyncSourceFile);
        return await _localDataAccessor.ReadFileContentsAsync<SyncSourceEntity>(filePath, cancellationToken);
    }
}
