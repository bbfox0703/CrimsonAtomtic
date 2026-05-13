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
}
