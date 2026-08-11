using EmuSync.Agent.Dto.Game;
using EmuSync.Services.LudusaviImporter;
using EmuSync.Services.Managers.Objects;
using EmuSync.Domain.Objects;

namespace EmuSync.Agent.Mapping;

public static class GameMapping
{
    /// <summary>
    /// Maps a <see cref="GameEntity"/> to a <see cref="GameDto"/>
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public static GameDto ToDto(this GameEntity entity)
    {
        return new()
        {
            Id = entity.Id,
            Name = entity.Name,
            AutoSync = entity.AutoSync,
            SyncSourceIdLocations = ToLegacyLocations(entity.SyncSourceIdLocations),
            SyncSourceIdLocationsV2 = ToDtoLocations(entity.SyncSourceIdLocations),
            LastSyncedFrom = entity.LastSyncedFrom,
            LastSyncTimeUtc = entity.LastSyncTimeUtc,
            StorageBytes = entity.StorageBytes,
            MaximumLocalGameBackups = entity.MaximumLocalGameBackups
        };
    }

    /// <summary>
    /// Maps a <see cref="FoundGame"/> to a <see cref="GameSuggestionDto"/>
    /// </summary>
    /// <param name="game"></param>
    /// <returns></returns>
    public static GameSuggestionDto ToDto(this FoundGame game)
    {
        return new()
        {
            Name = game.Name,
            SuggestedFolderPaths = game.SuggestedFolderPaths
        };
    }

    /// <summary>
    /// Maps a <see cref="LocalGameBackupManifestEntity"/> to a <see cref="GameBackupManifestDto"/>
    /// </summary>
    /// <param name="manifest"></param>
    /// <returns></returns>
    public static GameBackupManifestDto ToDto(this LocalGameBackupManifestEntity manifest)
    {
        return new()
        {
            Id = manifest.Id,
            BackupFileName = manifest.BackupFileName,
            CreatedOnUtc = manifest.CreatedOnUtc
        };
    }

    /// <summary>
    /// Maps a <see cref="GameEntity"/> to a <see cref="GameSummaryDto"/>
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public static GameSummaryDto ToSummaryDto(this GameEntity entity)
    {
        return new()
        {
            Id = entity.Id,
            Name = entity.Name,
            AutoSync = entity.AutoSync,
            MaximumLocalGameBackups = entity.MaximumLocalGameBackups,
            SyncSourceIdLocations = ToLegacyLocations(entity.SyncSourceIdLocations),
            SyncSourceIdLocationsV2 = ToDtoLocations(entity.SyncSourceIdLocations),
            LastSyncedFrom = entity.LastSyncedFrom,
            LastSyncTimeUtc = entity.LastSyncTimeUtc,
            StorageBytes = entity.StorageBytes,
        };
    }

    /// <summary>
    /// Maps a <see cref="IGameDto"/> to a <see cref="GameEntity"/>
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    public static GameEntity ToEntity(this IGameDto dto)
    {
        string id = default!;

        if (dto is UpdateGameDto updateDto)
        {
            id = updateDto.Id;
        }

        return new()
        {
            Id = id,
            Name = dto.Name,
            AutoSync = dto.AutoSync,
            SyncSourceIdLocations = dto.SyncSourceIdLocationsV2 != null
                ? ToDomainLocations(dto.SyncSourceIdLocationsV2)
                : dto.SyncSourceIdLocations?.ToDictionary(
                    x => x.Key,
                    x => new List<GamePathEntry> { new() { Path = x.Value } }),
            MaximumLocalGameBackups = dto.MaximumLocalGameBackups
        };
    }

    /// <summary>
    /// Maps a <see cref="QuickAddGameDto"/> to a <see cref="GameBulkUpsert"/>
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    public static GameBulkUpsert ToUpsert(this QuickAddGameDto dto)
    {
        return new()
        {
            ExistingGameId = dto.ExistingGameId,
            GameName = dto.GameName,
            AutoSync = dto.AutoSync,
            MaximumLocalGameBackups = dto.MaximumLocalGameBackups,
            Path = dto.Path
        };
    }

    private static Dictionary<string, string>? ToLegacyLocations(
        Dictionary<string, List<GamePathEntry>>? locations)
    {
        return locations?.Where(x => x.Value.Count > 0).ToDictionary(x => x.Key, x => x.Value[0].Path);
    }

    private static Dictionary<string, List<GamePathEntryDto>>? ToDtoLocations(
        Dictionary<string, List<GamePathEntry>>? locations)
    {
        return locations?.ToDictionary(
            x => x.Key,
            x => x.Value.Select(path => new GamePathEntryDto
            {
                Path = path.Path,
                IncludeFilters = [.. path.IncludeFilters],
                ExcludeFilters = [.. path.ExcludeFilters],
                Enabled = path.Enabled
            }).ToList());
    }

    private static Dictionary<string, List<GamePathEntry>> ToDomainLocations(
        Dictionary<string, List<GamePathEntryDto>> locations)
    {
        return locations.ToDictionary(
            x => x.Key,
            x => x.Value.Select(path => new GamePathEntry
            {
                Path = path.Path,
                IncludeFilters = [.. path.IncludeFilters],
                ExcludeFilters = [.. path.ExcludeFilters],
                Enabled = path.Enabled
            }).ToList());
    }
}
