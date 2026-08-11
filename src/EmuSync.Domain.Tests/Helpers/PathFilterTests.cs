using EmuSync.Domain.Helpers;

namespace EmuSync.Domain.Tests.Helpers;

public class PathFilterTests
{
    [Fact]
    public void Passes_EmptyFilters_IncludesEverything()
    {
        Assert.True(new PathFilter([], []).Passes("saves/game.dat"));
    }

    [Fact]
    public void Passes_ExcludeWinsOverInclude()
    {
        PathFilter filter = new(["**/*.sav"], ["backup/**"]);

        Assert.True(filter.Passes("slot/main.sav"));
        Assert.False(filter.Passes("backup/main.sav"));
        Assert.False(filter.Passes("slot/readme.txt"));
    }
}
