using System.Linq;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Integrity tests for the static <see cref="MountCatalog"/> that drives the
/// Tools → Unlock Mounts dialog. Pure data — no game install / save needed.
/// Guards the invariants the unlock flow relies on: sigil mounts must carry a
/// sigil itemKey, the dragon must carry its charKey + knowledge, and there
/// are no key collisions.
/// </summary>
public sealed class MountCatalogTests
{
    [Fact]
    public void All_HasTheSevenKnownMounts()
    {
        Assert.Equal(7, MountCatalog.All.Count);
        Assert.Equal(6, MountCatalog.All.Count(m => m.Kind == MountUnlockKind.SigilGrant));
        Assert.Single(MountCatalog.All, m => m.Kind == MountUnlockKind.DragonTransplant);
    }

    [Fact]
    public void SigilMounts_CarryASigilItemKey_AndNoStaleCharKeyDup()
    {
        foreach (var m in MountCatalog.All.Where(m => m.Kind == MountUnlockKind.SigilGrant))
        {
            Assert.True(m.SigilItemKey != 0,
                $"Sigil mount '{m.DisplayName}' must have a non-zero SigilItemKey.");
        }
        // Sigil itemKeys must be unique (a dup would grant the wrong sigil).
        var sigilKeys = MountCatalog.All
            .Where(m => m.Kind == MountUnlockKind.SigilGrant)
            .Select(m => m.SigilItemKey)
            .ToList();
        Assert.Equal(sigilKeys.Count, sigilKeys.Distinct().Count());
    }

    [Fact]
    public void Dragon_CarriesCharKeyAndKnowledge_ButNoSigil()
    {
        var dragon = MountCatalog.All.Single(m => m.Kind == MountUnlockKind.DragonTransplant);
        Assert.Equal(MountCatalog.DragonCharacterKey, dragon.CharacterKey);
        Assert.Equal(1000799u, dragon.CharacterKey);
        Assert.Equal(0u, dragon.SigilItemKey);

        // The proven "no-quests" dragon bundle is exactly 187 keys (matches
        // the reference editor's _unlock_dragon_mount_no_quests + the prior
        // in-game-confirmed RE session). A 2-key guess made the dragon show
        // but not summon — pin the count so it can't silently shrink again.
        Assert.Equal(187, MountCatalog.DragonKnowledgeKeys.Length);
        // 1000560 = Knowledge_Unique_Varnia_Dragon ("Blackstar"); 1000174 =
        // Knowledge_CallVehicle ("Summon Mount") — the obvious gates.
        Assert.Contains(1000560u, MountCatalog.DragonKnowledgeKeys);
        Assert.Contains(1000174u, MountCatalog.DragonKnowledgeKeys);
    }

    [Fact]
    public void NonZeroCharKeys_AreUnique()
    {
        var charKeys = MountCatalog.All
            .Select(m => m.CharacterKey)
            .Where(k => k != 0)
            .ToList();
        Assert.Equal(charKeys.Count, charKeys.Distinct().Count());
    }

    [Fact]
    public void QuestArtifactsContainerKey_IsFive()
    {
        // The sigil lives in Quest Artifacts (_inventoryKey=5), not Backpack.
        Assert.Equal(5u, MountCatalog.QuestArtifactsInventoryKey);
    }

    [Fact]
    public void DonorResourceName_MatchesEmbeddedLogicalName()
    {
        // The donor is embedded with this exact LogicalName in the .csproj;
        // a mismatch would surface only at runtime as "donor missing".
        var asm = typeof(MountCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream(MountCatalog.DragonDonorResourceName);
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0, "Embedded dragon donor save is empty.");
    }
}
