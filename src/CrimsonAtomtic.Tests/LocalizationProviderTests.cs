using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// End-to-end cover for the production name-resolution bootstrap.
///
/// <para>
/// The individual bridges each have their own live-install test, but until
/// game 2.01 nothing exercised <see cref="LocalizationProvider"/> itself —
/// and 2.01 broke exactly there: it renamed the gamedata directory and every
/// file extension, and split each language's PALOC blob into one file per
/// namespace, while the provider still hardcoded the old names. Every bridge
/// returned NOT_FOUND and no name resolved anywhere, with a green suite.
/// This test closes that gap: it drives the real entry point against the
/// real install, whichever layout that install ships.
/// </para>
/// </summary>
public sealed class LocalizationProviderTests
{
    [Fact]
    public void Bootstrap_LiveInstall_ResolvesThroughWhicheverArchiveLayoutShips()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        if (pamt is null)
        {
            return; // no install — skip cleanly, like every other live test
        }
        // <root>/<group>/0.pamt -> <root>
        var gameRoot = Path.GetDirectoryName(Path.GetDirectoryName(pamt));
        Assert.NotNull(gameRoot);

        using var loc = new LocalizationProvider(new NativePazExtractor());

        Assert.True(loc.TryBootstrapFromGameRoot(gameRoot),
            "bootstrap must find the gamedata tables and the English PALOC — "
            + "a false here is the 2.01-shaped failure: every extraction "
            + "NOT_FOUND because the archive layout moved");
        Assert.True(loc.IsLoaded);

        // iteminfo resolved: 6,573 items on 2.00, 6,813 on 2.01. A floor
        // rather than a pin — this test is about the path, not the content.
        Assert.True(loc.ItemCount > 5_000,
            $"expected >5k items from the iteminfo bridge, got {loc.ItemCount}");

        // English PALOC resolved. 2.01 splits it across 39 namespace files;
        // the count is the whole language either way (190,905 on live 2.01).
        Assert.True(loc.EntryCount > 100_000,
            $"expected >100k English PALOC entries, got {loc.EntryCount}");
        Assert.Contains(LocalizationProvider.DefaultLanguage, loc.AvailableLanguages);

        // More than one language discovered — proves the paloc probe walks
        // the group range under the shipped layout, not just group 0020.
        Assert.True(loc.AvailableLanguages.Count > 1,
            "expected several PALOC languages, got: "
            + string.Join(", ", loc.AvailableLanguages));

        // A sibling table bridge (characterinfo) loaded too, so the failure
        // mode where only iteminfo resolves would still be caught.
        Assert.True(loc.CharacterCount > 0,
            "characterinfo bridge did not load");
    }

    /// <summary>
    /// 2.04 ships <c>dlc/dlc_01/</c>, a second install root. Saves written
    /// by 2.04 hold rows only it defines, so a base-only bootstrap passed
    /// every other test here while the editor showed those rows blank.
    /// </summary>
    [Fact]
    public void Bootstrap_LiveInstall_ResolvesAndOffersDlcRows()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        if (pamt is null)
        {
            return;
        }
        var gameRoot = Path.GetDirectoryName(Path.GetDirectoryName(pamt))!;
        if (GameDataLayout.DlcNames(gameRoot).Count == 0)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(gameRoot));

        // An item only iteminfo.dlc_01 defines: table row and PALOC name.
        const uint DlcItem = 1005282;
        Assert.Equal("Diving_PlateArmor_Helm_01", loc.ItemInfoStringKey(DlcItem));
        Assert.False(string.IsNullOrEmpty(
            loc.LookupItemName(DlcItem, LocalizationProvider.DefaultLanguage)));
        Assert.NotNull(loc.GetItemMaxStackCount(DlcItem));
        Assert.NotNull(loc.LookupItemInfoSummary(DlcItem));
        // Its icon hash lives in stringinfo.dlc_01.
        var icon = loc.GetItemIconPathHash(DlcItem);
        Assert.NotNull(icon);
        Assert.False(string.IsNullOrEmpty(loc.ResolveStringInfoHash(icon!.Value)));

        // A key table with rows only its DLC copy holds.
        Assert.False(string.IsNullOrEmpty(loc.ResolveByFieldTypeName("FactionNodeKey", 1000417)));

        // The pickers walk this enumeration: the DLC item is in it, tagged
        // with its DLC, and the DLC rows that repeat a base key are not
        // listed a second time.
        var offered = Enumerable.Range(0, loc.ItemCount).Select(i => loc.GetItem(i)!.Value.ItemKey).ToList();
        Assert.Contains(DlcItem, offered);
        Assert.Equal(offered.Count, offered.Distinct().Count());
        Assert.Equal("dlc_01", loc.ItemDlcName(DlcItem));
        Assert.Null(loc.ItemDlcName(2200)); // Pyeonjeon_Arrow, a base item
    }

    /// <summary>
    /// An install without DLC, and one whose DLC files are broken, must
    /// bootstrap exactly as the base game. Built from directory junctions to
    /// the live install's base groups (no admin rights needed), plus a
    /// <c>dlc/dlc_99</c> whose manifests are garbage.
    /// </summary>
    [Fact]
    public void Bootstrap_WithoutOrWithBrokenDlc_LoadsTheBaseGame()
    {
        if (!File.Exists("crimson_rs.dll") || !OperatingSystem.IsWindows())
        {
            return;
        }
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        if (pamt is null)
        {
            return;
        }
        var liveRoot = Path.GetDirectoryName(Path.GetDirectoryName(pamt))!;
        // Junctions must sit on a local volume; next to the test binaries is.
        var root = Path.Combine(AppContext.BaseDirectory, $"nodlc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            foreach (var group in Directory.GetDirectories(liveRoot)
                         .Where(d => Path.GetFileName(d) is { Length: 4 } n && n.All(char.IsAsciiDigit)
                                     || Path.GetFileName(d) == "meta"))
            {
                CreateJunction(Path.Combine(root, Path.GetFileName(group)), group);
            }
            int baseItems;
            using (var loc = new LocalizationProvider(new NativePazExtractor()))
            {
                Assert.Empty(GameDataLayout.DlcNames(root));
                Assert.True(loc.TryBootstrapFromGameRoot(root));
                Assert.Null(loc.ItemInfoStringKey(1005282));
                Assert.Null(loc.ItemDlcName(1005282));
                baseItems = loc.ItemCount;
                Assert.True(baseItems > 5_000);
            }

            foreach (var group in new[] { GameDataLayout.GameDataGroup, "0020" })
            {
                var dir = Path.Combine(root, GameDataLayout.DlcDirectory, "dlc_99", group);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "0.pamt"), [0xde, 0xad, 0xbe, 0xef]);
            }
            using (var loc = new LocalizationProvider(new NativePazExtractor()))
            {
                Assert.Equal(["dlc_99"], GameDataLayout.DlcNames(root));
                Assert.True(loc.TryBootstrapFromGameRoot(root));
                Assert.Equal(baseItems, loc.ItemCount);
                Assert.False(string.IsNullOrEmpty(
                    loc.LookupItemName(2200, LocalizationProvider.DefaultLanguage)));
            }
        }
        finally
        {
            // Never a recursive delete over the junctions: each is removed
            // as a link (rmdir without /s), then only the fake dlc tree,
            // which holds real files, is deleted recursively.
            foreach (var link in Directory.GetDirectories(root)
                         .Where(d => new DirectoryInfo(d).LinkTarget is not null))
            {
                RunCmd("rmdir", link);
            }
            var dlcDir = Path.Combine(root, GameDataLayout.DlcDirectory);
            if (Directory.Exists(dlcDir))
            {
                Directory.Delete(dlcDir, recursive: true);
            }
            Directory.Delete(root);
        }
    }

    private static void CreateJunction(string link, string target) =>
        RunCmd("mklink", "/J", link, target);

    private static void RunCmd(params string[] args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", ["/c", .. args])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"{string.Join(' ', args)} failed: {p.StandardError.ReadToEnd()}");
    }

    /// <summary>
    /// Stage and quest load through their index: the headerless scan drops
    /// rows whose name fails its validation, and on 2.04 it misses 3,165
    /// stages. <c>StartGame</c> (quest key 1) is one such row.
    /// </summary>
    [Fact]
    public void Bootstrap_LiveInstall_KeepsRowsTheHeaderlessScanDrops()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return;
        }
        var pamt = LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup);
        if (pamt is null)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));

        Assert.False(string.IsNullOrEmpty(loc.ResolveByFieldTypeName("QuestKey", 1)));
    }

    [Fact]
    public void DlcNames_ListsDirectoriesWithAGameDataManifest_Sorted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"crimsonatomtic_dlc_{Guid.NewGuid():N}");
        try
        {
            Assert.Empty(GameDataLayout.DlcNames(root));
            foreach (var dlc in new[] { "dlc_02", "dlc_01" })
            {
                var group = Path.Combine(root, GameDataLayout.DlcDirectory, dlc, GameDataLayout.GameDataGroup);
                Directory.CreateDirectory(group);
                File.WriteAllBytes(Path.Combine(group, "0.pamt"), []);
            }
            Directory.CreateDirectory(Path.Combine(root, GameDataLayout.DlcDirectory, "not_a_dlc"));

            Assert.Equal(["dlc_01", "dlc_02"], GameDataLayout.DlcNames(root));
            Assert.Equal("iteminfo.dlc_01", GameDataLayout.DlcTableStem("iteminfo", "dlc_01"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
