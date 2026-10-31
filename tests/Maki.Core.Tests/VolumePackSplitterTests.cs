using System.IO.Compression;
using Maki.Core.Import;
using Maki.Core.Parsing;
using Maki.Core.Reading;

namespace Maki.Core.Tests;

/// <summary>
/// A multi-volume pack is split into one file per volume. The layout is the real
/// "Majo to yohei vol 01-08" pack: a wrapper folder named for the range, one folder per volume,
/// and volume 4's pages a folder deeper.
/// </summary>
public class VolumePackSplitterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "maki-split-" + Guid.NewGuid().ToString("N")[..8]);

    public VolumePackSplitterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly string[] Pack =
    [
        "DLRAW.TO_Majo to yohei vol 01-08/DLRAW.TO_Majo to yohei v01/DLRAW.TO_001.jpg",
        "DLRAW.TO_Majo to yohei vol 01-08/DLRAW.TO_Majo to yohei v01/DLRAW.TO_002.jpg",
        "DLRAW.TO_Majo to yohei vol 01-08/DLRAW.TO_Majo to yohei v02/DLRAW.TO_174.jpg",
        "DLRAW.TO_Majo to yohei vol 01-08/DLRAW.TO_Majo to yohei v04/DLRAW.TO_魔女と傭兵（4）/DLRAW.TO_186.jpg",
        "DLRAW.TO_Majo to yohei vol 01-08/DLRAW.TO_Majo_to_yohei_v08/DLRAW.TO_178.jpg"
    ];

    private static ComicSource Source(string path, params string[] pages) =>
        new("Pack.cbz", ComicSourceKind.Cbz, path, 1000, pages);

    [Fact]
    public void Splits_into_one_source_per_volume_in_volume_order()
    {
        var parts = VolumePackSplitter.Split(Source("pack.cbz", Pack));

        Assert.Equal(
            ["DLRAW.TO_Majo to yohei v01.cbz", "DLRAW.TO_Majo to yohei v02.cbz",
             "DLRAW.TO_Majo to yohei v04.cbz", "DLRAW.TO_Majo_to_yohei_v08.cbz"],
            parts.Select(p => p.Name));
        Assert.Equal([1, 2, 4, 8], parts.Select(p => ReleaseNameParser.ParseFileName(p.Name).Volume!.Value));
        Assert.Equal([2, 1, 1, 1], parts.Select(p => p.Pages.Count));
        Assert.All(parts, p => Assert.False(p.IsReadyToPlace));
    }

    [Theory]
    [InlineData("Title v01/001.jpg")]
    [InlineData("001.jpg")]
    public void Leaves_a_single_volume_whole(string page)
    {
        var source = Source("one.cbz", page, page.Replace("001", "002"));
        Assert.Same(source, Assert.Single(VolumePackSplitter.Split(source)));
    }

    [Fact]
    public void Leaves_the_pack_whole_when_a_page_sits_outside_every_volume_folder()
    {
        var source = Source("pack.cbz", [.. Pack, "DLRAW.TO_Majo to yohei vol 01-08/cover.jpg"]);
        Assert.Same(source, Assert.Single(VolumePackSplitter.Split(source)));
    }

    [Fact]
    public void Leaves_the_pack_whole_when_two_folders_read_as_the_same_volume()
    {
        var source = Source("pack.cbz", "A v01/1.jpg", "B v01/1.jpg");
        Assert.Same(source, Assert.Single(VolumePackSplitter.Split(source)));
    }

    [Fact]
    public void Written_volume_holds_only_its_own_pages_without_the_volume_folder()
    {
        var pack = Path.Combine(_root, "Majo to yohei vol 01-08.cbz");
        using (var archive = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            foreach (var name in Pack)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(name);
            }
        }

        var parts = ComicSourceScanner.Scan(pack);
        Assert.Equal(4, parts.Count);

        var target = Path.Combine(_root, "out", "v04.cbz");
        ComicSourceConverter.Materialize(parts.Single(p => p.Name.EndsWith("v04.cbz")), target, useHardlinks: false);

        using var written = ZipFile.OpenRead(target);
        var entry = Assert.Single(written.Entries);
        Assert.Equal("DLRAW.TO_魔女と傭兵（4）/DLRAW.TO_186.jpg", entry.FullName);
        Assert.True(File.Exists(pack), "the pack itself is never touched");
    }
}
