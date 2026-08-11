namespace EmuSync.Domain.Objects;

public record GamePathEntry
{
    public string Path { get; set; } = string.Empty;
    public List<string> IncludeFilters { get; set; } = [];
    public List<string> ExcludeFilters { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
