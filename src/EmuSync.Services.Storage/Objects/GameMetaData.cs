using EmuSync.Domain.Entities;
using EmuSync.Domain.Objects;
using System.Text.Json.Serialization;

namespace EmuSync.Services.Storage.Objects;

public record GameMetaData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("b")]
    public string Name { get; set; }

    [JsonPropertyName("as")]
    public bool AutoSync { get; set; }

    [JsonPropertyName("sl")]
    public Dictionary<string, string>? SyncSourceIdLocations { get; set; }

    [JsonPropertyName("sl2")]
    public Dictionary<string, List<GamePathEntry>>? SyncSourceIdLocationsV2 { get; set; }

    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("lsf")]
    public string? LastSyncedFrom { get; set; }

    [JsonPropertyName("lst")]
    public DateTime? LastSyncTimeUtc { get; set; }

    [JsonPropertyName("lwt")]
    public DateTime? LatestWriteTimeUtc { get; set; }

    [JsonPropertyName("sb")]
    public long StorageBytes { get; set; }

    [JsonPropertyName("mlgb")]
    public int? MaximumLocalGameBackups { get; set; }

    public GameEntity ToEntity()
    {
        return new()
        {
            Id = this.Id,
            Name = this.Name,
            AutoSync = this.AutoSync,
            LastSyncTimeUtc = this.LastSyncTimeUtc,
            LastSyncedFrom = this.LastSyncedFrom,
            LatestWriteTimeUtc = this.LatestWriteTimeUtc,
            SyncSourceIdLocations = SyncSourceIdLocationsV2 != null
                ? CloneLocations(SyncSourceIdLocationsV2)
                : SyncSourceIdLocations?.ToDictionary(
                    x => x.Key,
                    x => new List<GamePathEntry> { new() { Path = x.Value } }),
            StorageBytes = this.StorageBytes,
            MaximumLocalGameBackups = this.MaximumLocalGameBackups,
        };
    }

    public static GameMetaData FromGame(GameEntity entity)
    {
        return new()
        {
            Id = entity.Id,
            Name = entity.Name,
            AutoSync = entity.AutoSync,
            SyncSourceIdLocations = entity.SyncSourceIdLocations?.Where(x => x.Value.Count > 0)
                .ToDictionary(x => x.Key, x => x.Value[0].Path),
            SyncSourceIdLocationsV2 = CloneLocations(entity.SyncSourceIdLocations),
            Version = 2,
            LastSyncTimeUtc = entity.LastSyncTimeUtc,
            LastSyncedFrom = entity.LastSyncedFrom,
            LatestWriteTimeUtc = entity.LatestWriteTimeUtc,
            StorageBytes = entity.StorageBytes,
            MaximumLocalGameBackups = entity.MaximumLocalGameBackups,
        };
    }

    private static Dictionary<string, List<GamePathEntry>>? CloneLocations(
        Dictionary<string, List<GamePathEntry>>? locations)
    {
        return locations?.ToDictionary(
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
