using CrimsonAtomtic.Ui.Platform;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure tests for the install-probe helpers: Steam libraryfolders.vdf
/// parsing, Crimson Desert witness-file validation, and the Epic
/// manifest matching rule. No filesystem dependencies for the parser
/// tests; the validator tests work against a self-contained temp dir.
/// </summary>
public sealed class InstallProbeTests : IDisposable
{
    private readonly string _scratch;

    public InstallProbeTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"crimson-install-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }
        catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public void Vdf_ExtractsAllLibraryPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"contentid"		"1234567890"
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            		"label"		""
            	}
            	"2"
            	{
            		"path"		"E:\\Games\\Steam Library"
            		"label"		""
            	}
            }
            """;

        var paths = SteamLibraryProbe.ExtractLibraryPaths(vdf).ToList();

        Assert.Equal(3, paths.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        Assert.Equal(@"D:\SteamLibrary", paths[1]);
        Assert.Equal(@"E:\Games\Steam Library", paths[2]);
    }

    [Fact]
    public void Vdf_EmptyText_YieldsNoPaths()
    {
        Assert.Empty(SteamLibraryProbe.ExtractLibraryPaths(string.Empty));
    }

    [Fact]
    public void Vdf_NoLibraryPaths_YieldsEmpty()
    {
        // Real Steam config sometimes has libraryfolders pointing at
        // zero libraries (fresh install) — the file still parses cleanly
        // but yields nothing useful.
        const string vdf = """
            "libraryfolders"
            {
            }
            """;

        Assert.Empty(SteamLibraryProbe.ExtractLibraryPaths(vdf));
    }

    [Fact]
    public void Vdf_UnescapesBackslashes()
    {
        // VDF doubles backslashes inside string values. After unescape,
        // exactly one backslash per separator is the expected shape on
        // Windows.
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"F:\\Games\\Steam Library\\Crimson"
            	}
            }
            """;

        var paths = SteamLibraryProbe.ExtractLibraryPaths(vdf).Single();
        Assert.Equal(@"F:\Games\Steam Library\Crimson", paths);
    }

    [Fact]
    public void LooksLikeCrimsonDesertInstall_WitnessPresent_ReturnsTrue()
    {
        var install = Path.Combine(_scratch, "Crimson Desert");
        Directory.CreateDirectory(Path.Combine(install, "0020"));
        File.WriteAllBytes(Path.Combine(install, "0020", "0.pamt"), new byte[] { 0x00 });

        Assert.True(SteamLibraryProbe.LooksLikeCrimsonDesertInstall(install));
    }

    [Fact]
    public void LooksLikeCrimsonDesertInstall_NoWitness_ReturnsFalse()
    {
        // Empty directory should not be misidentified as a valid install.
        var install = Path.Combine(_scratch, "EmptyDir");
        Directory.CreateDirectory(install);

        Assert.False(SteamLibraryProbe.LooksLikeCrimsonDesertInstall(install));
    }

    [Fact]
    public void LooksLikeCrimsonDesertInstall_PartialWitness_ReturnsFalse()
    {
        // 0020 dir exists but no PAMT inside — a cancelled / partial
        // install. Must not be accepted.
        var install = Path.Combine(_scratch, "PartialInstall");
        Directory.CreateDirectory(Path.Combine(install, "0020"));

        Assert.False(SteamLibraryProbe.LooksLikeCrimsonDesertInstall(install));
    }

    [Fact]
    public void LooksLikeCrimsonDesertInstall_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(SteamLibraryProbe.LooksLikeCrimsonDesertInstall(string.Empty));
        Assert.False(SteamLibraryProbe.LooksLikeCrimsonDesertInstall("   "));
    }

    [Fact]
    public void EpicManifest_DisplayNameMatch_ReturnsTrue()
    {
        var entry = new EpicManifestEntry
        {
            DisplayName = "Crimson Desert",
            AppName = "abc123",
            InstallLocation = @"C:\Games\Epic\CrimsonDesert",
        };
        Assert.True(EpicManifestProbe.IsCrimsonDesertManifest(entry));
    }

    [Fact]
    public void EpicManifest_DisplayNameMatchCaseInsensitive_ReturnsTrue()
    {
        var entry = new EpicManifestEntry
        {
            DisplayName = "crimson DESERT",
            InstallLocation = @"C:\Games\Epic\CrimsonDesert",
        };
        Assert.True(EpicManifestProbe.IsCrimsonDesertManifest(entry));
    }

    [Fact]
    public void EpicManifest_AppNameMatch_ReturnsTrue()
    {
        // DisplayName doesn't mention the game (e.g. a localized name)
        // but AppName contains the canonical token.
        var entry = new EpicManifestEntry
        {
            DisplayName = "검은 사막: 크림슨 데저트",
            AppName = "CrimsonDesertGame",
            InstallLocation = @"C:\Games\Epic\CrimsonDesert",
        };
        Assert.True(EpicManifestProbe.IsCrimsonDesertManifest(entry));
    }

    [Fact]
    public void EpicManifest_UnrelatedGame_ReturnsFalse()
    {
        var entry = new EpicManifestEntry
        {
            DisplayName = "Fortnite",
            AppName = "Fortnite",
            InstallLocation = @"C:\Games\Epic\Fortnite",
        };
        Assert.False(EpicManifestProbe.IsCrimsonDesertManifest(entry));
    }

    [Fact]
    public void EpicManifest_EmptyEntry_ReturnsFalse()
    {
        var entry = new EpicManifestEntry();
        Assert.False(EpicManifestProbe.IsCrimsonDesertManifest(entry));
    }
}
