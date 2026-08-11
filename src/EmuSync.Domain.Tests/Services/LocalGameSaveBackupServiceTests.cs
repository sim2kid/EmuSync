using EmuSync.Domain.Entities;
using EmuSync.Domain.Services;
using EmuSync.Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EmuSync.Domain.Tests.Services;

public class LocalGameSaveBackupServiceTests
{
    private static LocalGameSaveBackupService CreateSut(Mock<ILocalDataAccessor>? local = null)
    {
        var logger = new Mock<ILogger<LocalGameSaveBackupService>>();
        local ??= new Mock<ILocalDataAccessor>();

        return new LocalGameSaveBackupService(logger.Object, local.Object);
    }

    private static Mock<ILocalDataAccessor> CreateLocalAccessorReturningNoManifest()
    {
        var local = new Mock<ILocalDataAccessor>();
        local.Setup(x => x.ReadFileContentsOrDefaultAsync<List<LocalGameBackupManifestEntity>>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
             .ReturnsAsync((List<LocalGameBackupManifestEntity>?)null);
        return local;
    }

    [Fact]
    public async Task GetBackupsAsync_WhenNoManifest_ReturnsEmpty()
    {
        var local = CreateLocalAccessorReturningNoManifest();
        var sut = CreateSut(local);

        var result = await sut.GetBackupsAsync("g1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RestoreBackupAsync_WhenNoManifest_ThrowsInvalidOperationException()
    {
        var local = CreateLocalAccessorReturningNoManifest();
        var sut = CreateSut(local);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.RestoreBackupAsync("g1", "b1", []));
    }

    [Fact]
    public async Task DeleteBackupAsync_WhenNoManifest_ThrowsInvalidOperationException()
    {
        var local = CreateLocalAccessorReturningNoManifest();
        var sut = CreateSut(local);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.DeleteBackupAsync("g1", "b1"));
    }
}
