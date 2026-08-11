using System.Text.Json.Serialization;

namespace EmuSync.Agent.Dto.Game;

public record GamePathEntryDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("includeFilters")]
    public List<string> IncludeFilters { get; set; } = [];

    [JsonPropertyName("excludeFilters")]
    public List<string> ExcludeFilters { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
