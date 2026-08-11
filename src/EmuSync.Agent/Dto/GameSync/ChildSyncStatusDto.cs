using System.Text.Json.Serialization;

namespace EmuSync.Agent.Dto.GameSync;

public record ChildSyncStatusDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("latestWriteTimeUtc")]
    public DateTime? LatestWriteTimeUtc { get; set; }

    [JsonPropertyName("errors")]
    public List<ChildSyncErrorDto> Errors { get; set; } = [];
}

public record ChildSyncErrorDto
{
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
