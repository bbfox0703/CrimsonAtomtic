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
}
