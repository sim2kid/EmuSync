using System.IO.Compression;
using EmuSync.Domain.Helpers;
using EmuSync.Domain.Objects;

namespace EmuSync.Domain.Tests.Helpers;

public class CombinedZipTests
{
    [Fact]
    public void CombinedZip_RoutesChildrenAndAppliesFilters()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        string outputFirst = Path.Combine(root, "output-first");
        string outputSecond = Path.Combine(root, "output-second");
        string zip = Path.Combine(root, "game.zip");

        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "save.dat"), "first");
            File.WriteAllText(Path.Combine(first, "ignored.tmp"), "ignored");
            File.WriteAllText(Path.Combine(second, "save.dat"), "second");

            ZipHelper.CreateCombinedZip([
                new() { Path = first, ExcludeFilters = ["*.tmp"] },
                new() { Path = second }
            ], zip);

            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                Assert.NotNull(archive.GetEntry("manifest.json"));
                Assert.NotNull(archive.GetEntry("0/save.dat"));
                Assert.NotNull(archive.GetEntry("1/save.dat"));
                Assert.Null(archive.GetEntry("0/ignored.tmp"));
            }

            using FileStream stream = File.OpenRead(zip);
            ZipHelper.ExtractCombinedZip(stream, [
                new() { Path = outputFirst },
                new() { Path = outputSecond }
            ]);

            Assert.Equal("first", File.ReadAllText(Path.Combine(outputFirst, "save.dat")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(outputSecond, "save.dat")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
