using Moq;
using Microsoft.Extensions.Logging;
using EmuSync.Domain.Entities;
using EmuSync.Domain.Enums;
using EmuSync.Domain.Results;
using EmuSync.Domain.Objects;
using EmuSync.Domain.Services.Interfaces;
using EmuSync.Services.Managers;
using EmuSync.Services.Managers.Interfaces;
using EmuSync.Services.Managers.Results;
using EmuSync.Services.Storage.Interfaces;
using Xunit;

namespace EmuSync.Services.Managers.Tests;

public class GameSyncManagerTests
{
    private readonly Mock<ILogger<GameSyncManager>> _logger = new();
    private readonly Mock<ILocalDataAccessor> _local = new();
    private readonly Mock<IStorageProviderFactory> _factory = new();
    private readonly Mock<IGameManager> _gameManager = new();
    private readonly Mock<ILocalSyncLog> _localSyncLog = new();
    private readonly Mock<ILocalGameSaveBackupService> _backupService = new();
    private readonly Mock<ISyncProgressTracker> _progressTracker = new();

    private GameSyncManager CreateSut() => new(
        _logger.Object,
        _local.Object,
        _factory.Object,
        _gameManager.Object,
        _localSyncLog.Object,
        _backupService.Object,
        _progressTracker.Object
    );

    [Fact]
    public void GetSyncType_NoLastSync_AndDirExists_Returns_RequiresUpload()
    {
        var game = CreateGame();
        var scan = new DirectoryScanResult { DirectoryExists = true, DirectoryIsSet = true };

        _local.Setup(x =>
            x.ScanDirectories(
                It.IsAny<IEnumerable<GamePathEntry>>()
            )
        ).Returns(scan);

        var sut = CreateSut();
        var result = sut.GetSyncType("s1", game);

        Assert.Equal(GameSyncStatus.RequiresUpload, result.SyncStatus);
    }

    [Fact]
    public void GetSyncType_NoDirectorySet_Returns_UnsetDirectory()
    {
        var game = CreateGame();
        game.LastSyncTimeUtc = DateTime.UtcNow;
        var scan = new DirectoryScanResult { DirectoryIsSet = false };

        _local.Setup(x =>
            x.ScanDirectories(
                It.IsAny<IEnumerable<GamePathEntry>>()
            )
        ).Returns(scan);

        var sut = CreateSut();
        var result = sut.GetSyncType("s1", game);

        Assert.Equal(GameSyncStatus.UnsetDirectory, result.SyncStatus);
    }

    [Fact]
    public void GetSyncType_LocalMissing_Returns_RequiresDownload()
    {
        var game = CreateGame();
        game.LastSyncTimeUtc = DateTime.UtcNow;
        var scan = new DirectoryScanResult
        {
            DirectoryIsSet = true,
            DirectoryExists = false,
            LatestDirectoryWriteTimeUtc = null
        };

        _local.Setup(x =>
            x.ScanDirectories(
                It.IsAny<IEnumerable<GamePathEntry>>()
            )
        ).Returns(scan);

        var sut = CreateSut();
        var result = sut.GetSyncType("s1", game);

        Assert.Equal(GameSyncStatus.RequiresDownload, result.SyncStatus);
    }

    [Fact]
    public void GetSyncType_LocalNewerThanCloud_Returns_RequiresUpload()
    {
        var game = new GameEntity
        {
            Id = "g1",
            SyncSourceIdLocations = new() { { "s1", [new() { Path = "p" }] } },
            LastSyncTimeUtc = DateTime.UtcNow.AddHours(-2),
            LatestWriteTimeUtc = DateTime.UtcNow.AddHours(-2)
        };

        var scan = new DirectoryScanResult
        {
            DirectoryIsSet = true,
            DirectoryExists = true,
            LatestDirectoryWriteTimeUtc = DateTime.UtcNow
        };

        _local.Setup(x =>
            x.ScanDirectories(
                It.IsAny<IEnumerable<GamePathEntry>>()
            )
        ).Returns(scan);

        var sut = CreateSut();
        var result = sut.GetSyncType("s1", game);

        Assert.Equal(GameSyncStatus.RequiresUpload, result.SyncStatus);
    }

    [Fact]
    public async Task ForceDownloadGameAsync_Throws_When_NoPath()
    {
        var game = new GameEntity { Id = "g1" };
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.ForceDownloadGameAsync("s1", game, true)
        );
    }

    [Fact]
    public async Task ForceUploadGameAsync_Throws_When_NoPath()
    {
        var game = new GameEntity { Id = "g1" };
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.ForceUploadGameAsync("s1", game, true)
        );
    }

    private static GameEntity CreateGame() => new()
    {
        Id = "g1",
        SyncSourceIdLocations = new() { { "s1", [new() { Path = "p" }] } }
    };
}
