using System.Text.Json.Serialization;

namespace EmuSync.Agent.Dto.Game;

public record GameSummaryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("autoSync")]
    public bool AutoSync { get; set; }

    [JsonPropertyName("maximumLocalGameBackups")]
    public int? MaximumLocalGameBackups { get; set; }

    [JsonPropertyName("syncSourceIdLocations")]
    public Dictionary<string, string>? SyncSourceIdLocations { get; set; }

    [JsonPropertyName("syncSourceIdLocationsV2")]
    public Dictionary<string, List<GamePathEntryDto>>? SyncSourceIdLocationsV2 { get; set; }

    [JsonPropertyName("lastSyncedFrom")]
    public string? LastSyncedFrom { get; set; }

    [JsonPropertyName("lastSyncTimeUtc")]
    public DateTime? LastSyncTimeUtc { get; set; }

    [JsonPropertyName("syncStatusId")]
    public int SyncStatusId { get; set; }

    [JsonPropertyName("storageBytes")]
    public long? StorageBytes { get; set; }
}
