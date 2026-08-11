using EmuSync.Domain.Entities;
using EmuSync.Domain.Objects;

namespace EmuSync.Domain.Services.Interfaces;

public interface ILocalGameSaveBackupService
{
    Task<List<LocalGameBackupManifestEntity>> GetBackupsAsync(string gameId, CancellationToken cancellationToken = default);
    Task CreateBackupAsync(GameEntity game, IReadOnlyList<GamePathEntry> paths, Action<double>? onProgress = null, CancellationToken cancellationToken = default);
    Task DeleteBackupAsync(string gameId, string backupId, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string gameId, string backupFileName, IReadOnlyList<GamePathEntry> paths, CancellationToken cancellationToken = default);
}
