using EmuSync.Domain.Enums;
using EmuSync.Domain.Results;
using EmuSync.Domain.Objects;

namespace EmuSync.Services.Managers.Results;

public record GetSyncTypeResult
{
    public GameSyncStatus SyncStatus { get; set; }
    public List<GamePathEntry> Children { get; set; } = [];
    public DirectoryScanResult DirectoryScanResult { get; set; } = new();

    public bool NoLocalFolderPath => Children.Count == 0;
}
