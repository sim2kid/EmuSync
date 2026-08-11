using System;
using System.Collections.Generic;
using System.Text.Json;
using EmuSync.Services.Storage.Objects;
using EmuSync.Domain.Entities;
using EmuSync.Domain.Objects;
using Xunit;

namespace EmuSync.Services.Storage.Tests.Objects;

public class GameMetaDataTests
{
    [Fact]
    public void SerializesCorrectly()
    {
        var now = DateTime.UtcNow;

        var obj = new GameMetaData
        {
            Id = "g1",
            Name = "N",
            AutoSync = true,
            SyncSourceIdLocations = new Dictionary<string, string> { { "s", "p" } },
            LastSyncedFrom = "x",
            LastSyncTimeUtc = now,
            LatestWriteTimeUtc = now,
            StorageBytes = 10,
            MaximumLocalGameBackups = 2
        };

        var json = JsonSerializer.Serialize(obj);

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"b\"", json); // short name property
    }

    [Fact]
    public void DeserializesCorrectly()
    {
        var json = """
        {
          "id": "g1",
          "b": "N",
          "as": true,
          "sl": { "s": "p" },
          "lsf": "x",
          "lst": "2020-01-01T00:00:00Z",
          "lwt": "2020-01-02T00:00:00Z",
          "sb": 10,
          "mlgb": 2
        }
        """;

        var obj = JsonSerializer.Deserialize<GameMetaData>(json);

        Assert.NotNull(obj);
        Assert.Equal("g1", obj.Id);
        Assert.Equal("N", obj.Name);
        Assert.True(obj.AutoSync);
        Assert.Equal(10, obj.StorageBytes);
        Assert.Equal("p", obj.ToEntity().SyncSourceIdLocations!["s"][0].Path);
    }

    [Fact]
    public void NewShape_RoundTripsAndWritesLegacyProjection()
    {
        GameEntity game = new()
        {
            Id = "g1",
            Name = "Game",
            SyncSourceIdLocations = new()
            {
                { "s", [new() { Path = "one", ExcludeFilters = ["*.tmp"] }, new() { Path = "two" }] }
            }
        };

        GameMetaData metadata = GameMetaData.FromGame(game);
        GameEntity restored = metadata.ToEntity();

        Assert.Equal(2, metadata.Version);
        Assert.Equal("one", metadata.SyncSourceIdLocations!["s"]);
        Assert.Equal("*.tmp", restored.SyncSourceIdLocations!["s"][0].ExcludeFilters[0]);
        Assert.Equal("two", restored.SyncSourceIdLocations["s"][1].Path);
    }
}
