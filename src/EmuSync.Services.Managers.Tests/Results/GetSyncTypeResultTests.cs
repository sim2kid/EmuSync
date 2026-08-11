using EmuSync.Domain.Enums;
using EmuSync.Services.Managers.Results;
using EmuSync.Domain.Objects;

namespace EmuSync.Services.Managers.Tests.Results;

public class GetSyncTypeResultTests
{
    [Fact]
    public void NoLocalFolderPath_ReflectsEnabledChildren()
    {
        var result = new GetSyncTypeResult { SyncStatus = GameSyncStatus.Unknown };
        Assert.True(result.NoLocalFolderPath);

        result = new GetSyncTypeResult { Children = [new GamePathEntry { Path = "p" }] };
        Assert.False(result.NoLocalFolderPath);
    }
}
