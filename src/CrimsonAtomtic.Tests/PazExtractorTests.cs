using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Tests for the C# binding over <c>crimson_paz_extract_file</c>.
/// The "happy path" requires a real Crimson Desert install (same
/// pattern as the save-loader live tests); error paths run on any
/// machine.
/// </summary>
public sealed class PazExtractorTests
{
    private static string? FindEnglishPamt()
    {
        // Mirror the probe order in WindowsPlatformPaths.GameInstallRoot.
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            var p = Path.Combine(root, "0020", "0.pamt");
            if (File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    private static string? FindIconsPamt()
    {
        // 0012 ships every icon DDS — including the partial-compressed ones
        // (~97% of the directory) the new decompressor unblocks.
        string[] candidates =
        [
            @"D:\SteamLibrary\steamapps\common\Crimson Desert",
            @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
            @"C:\Program Files\Steam\steamapps\common\Crimson Desert",
            @"E:\SteamLibrary\steamapps\common\Crimson Desert",
            @"F:\SteamLibrary\steamapps\common\Crimson Desert",
        ];
        foreach (var root in candidates)
        {
            var p = Path.Combine(root, "0012", "0.pamt");
            if (File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>
    /// End-to-end check that the C# binding now extracts partial-compressed
    /// icon DDS files (PAZ flag <c>raw_compression == 1</c>). Picks two
    /// well-known icons from <c>0012/ui/texture/icon/</c> — pre-Phase 3 these
    /// would have surfaced as <c>BODY_PARSE</c> from the Rust side. Skips
    /// cleanly when no install / dll is present.
    /// </summary>
    [Theory]
    // LZ4-compressed: exercises the header(128)+lz4(prefix-dict) decoder.
    [InlineData("ui/texture/icon", "cd_icon_skill_07.dds")]
    // Identity-stored: exercises the c==u fast path.
    [InlineData("ui/texture/icon", "icon_item_collection_prop_statue_0001.dds")]
    public void ExtractFile_LiveInstall_PartialCompressedIconExtractsAsValidDds(
        string directory, string fileName)
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindIconsPamt();
        if (pamt is null)
        {
            return;
        }

        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(pamt, directory, fileName);

        Assert.NotEmpty(bytes);
        // DDS magic: 'D' 'D' 'S' ' ' (0x20534444 LE).
        Assert.True(
            bytes.Length >= 4 && bytes[0] == 0x44 && bytes[1] == 0x44
                && bytes[2] == 0x53 && bytes[3] == 0x20,
            $"expected DDS magic at start of {directory}/{fileName}, got " +
                $"[{bytes[0]:x2} {bytes[1]:x2} {bytes[2]:x2} {bytes[3]:x2}]");
        // DDS header itself is 124 bytes after the 4-byte magic, so any
        // valid texture is at least 128 bytes long.
        Assert.True(bytes.Length >= 128,
            $"{directory}/{fileName} truncated: only {bytes.Length} bytes");
    }

    [Fact]
    public void ExtractFile_LiveInstall_ExtractsEnglishPaloc()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindEnglishPamt();
        if (pamt is null)
        {
            return; // no game install — skip cleanly
        }

        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt,
            "gamedata/stringtable/binary__",
            "localizationstring_eng.paloc");

        Assert.NotEmpty(bytes);
        // The extracted bytes must parse as a PALOC and yield a
        // plausibly-populated catalog (1.06 ships ~100k entries).
        using var cat = NativePalocCatalog.LoadFromBytes(bytes);
        Assert.True(cat.EntryCount > 10_000,
            $"expected >10k English PALOC entries, got {cat.EntryCount}");
    }

    [Fact]
    public void ExtractFile_NotFoundDirectory_ThrowsCrimsonSaveException()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = FindEnglishPamt();
        if (pamt is null)
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            extractor.ExtractFile(pamt, "not/a/real/dir", "x.bin"));
        Assert.Equal(-16, ex.ErrorCode); // NOT_FOUND
    }

    [Fact]
    public void ExtractFile_BadPamtPath_ThrowsWithIoErrorCode()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var ex = Assert.Throws<CrimsonSaveException>(() =>
            extractor.ExtractFile(
                @"Z:\definitely\does\not\exist\0.pamt",
                "anything",
                "anything"));
        Assert.Equal(-3, ex.ErrorCode); // IO
    }

    [Fact]
    public void ExtractFile_RejectsNullOrEmptyArgs()
    {
        var extractor = new NativePazExtractor();
        Assert.Throws<ArgumentException>(() => extractor.ExtractFile("", "x", "y"));
        Assert.Throws<ArgumentNullException>(() => extractor.ExtractFile("x", null!, "y"));
        Assert.Throws<ArgumentException>(() => extractor.ExtractFile("x", "y", ""));
    }

    /// <summary>
    /// Sanity-check that every PALOC language code the UI advertises
    /// actually resolves against the live install. Typos in
    /// <c>LocalizationProvider.KnownLanguageCodes</c> (e.g. the historical
    /// <c>fra</c>/<c>por</c>/<c>spa</c> mismatch with the game's actual
    /// <c>fre</c>/<c>por-br</c>/<c>spa-es</c>/<c>spa-mx</c>) silently
    /// drop a language from the secondary-language picker without
    /// failing anything — this test catches that class of regression.
    /// Skips cleanly when no game install is reachable.
    /// </summary>
    [Theory]
    [InlineData("kor",    19)]
    [InlineData("eng",    20)]
    [InlineData("jpn",    21)]
    [InlineData("rus",    22)]
    [InlineData("tur",    23)]
    [InlineData("spa-es", 24)]
    [InlineData("spa-mx", 25)]
    [InlineData("fre",    26)]
    [InlineData("ger",    27)]
    [InlineData("ita",    28)]
    [InlineData("pol",    29)]
    [InlineData("por-br", 30)]
    [InlineData("zho-tw", 31)]
    [InlineData("zho-cn", 32)]
    public void ExtractFile_LiveInstall_EveryAdvertisedLanguageResolves(string code, int group)
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var installRoot = FindEnglishPamt() is { } p
            ? Path.GetDirectoryName(Path.GetDirectoryName(p))
            : null;
        if (installRoot is null)
        {
            return;
        }
        var pamt = Path.Combine(installRoot, $"{group:D4}", "0.pamt");
        if (!File.Exists(pamt))
        {
            return;
        }
        var extractor = new NativePazExtractor();
        var bytes = extractor.ExtractFile(
            pamt,
            "gamedata/stringtable/binary__",
            $"localizationstring_{code}.paloc");
        Assert.NotEmpty(bytes);
        using var cat = NativePalocCatalog.LoadFromBytes(bytes);
        // Each translated table is on the order of 100k+ entries —
        // anything significantly smaller probably means the wrong file
        // was extracted (truncated, wrong format, etc).
        Assert.True(cat.EntryCount > 50_000,
            $"localizationstring_{code}.paloc: expected >50k entries, got {cat.EntryCount}");
    }
}
