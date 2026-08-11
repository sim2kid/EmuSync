using Microsoft.Extensions.FileSystemGlobbing;

namespace EmuSync.Domain.Helpers;

public sealed class PathFilter
{
    private readonly Matcher? _includes;
    private readonly Matcher? _excludes;

    public PathFilter(IEnumerable<string>? includeFilters, IEnumerable<string>? excludeFilters)
    {
        _includes = CreateMatcher(includeFilters);
        _excludes = CreateMatcher(excludeFilters);
    }

    public bool Passes(string relativePath)
    {
        string path = relativePath.Replace('\\', '/').TrimStart('/');
        bool included = _includes == null || _includes.Match(path).HasMatches;
        return included && (_excludes == null || !_excludes.Match(path).HasMatches);
    }

    public static void ValidatePattern(string pattern)
    {
        _ = CreateMatcher([pattern]);
    }

    private static Matcher? CreateMatcher(IEnumerable<string>? patterns)
    {
        string[] values = patterns?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalisePattern)
            .ToArray() ?? [];

        if (values.Length == 0) return null;

        Matcher matcher = new(OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

        foreach (string pattern in values)
        {
            try
            {
                matcher.AddInclude(pattern);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new ArgumentException($"Invalid glob pattern '{pattern}'.", nameof(patterns), ex);
            }
        }

        return matcher;
    }

    private static string NormalisePattern(string pattern)
    {
        string value = pattern.Trim().Replace('\\', '/').TrimStart('/');
        return value.EndsWith('/') ? $"{value}**" : value;
    }
}
