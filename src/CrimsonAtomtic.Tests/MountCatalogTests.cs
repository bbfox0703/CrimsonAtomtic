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
    public void DragonElementTemplate_IsAMercenaryTemplateForTheDragon()
    {
        // A crimson-rs element template (CRET v1) exported from the captured
        // element: magic + version, then the MercenarySaveData object.
        // MountUnlockMechanicsTests builds it into real saves.
        var bytes = Convert.FromHexString(MountCatalog.DragonElementTemplateHex);
        Assert.Equal(654, bytes.Length);
        Assert.Equal("CRET"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, bytes[4]);
        var className = "MercenarySaveData"u8.ToArray();
        Assert.Equal(className.Length, BitConverter.ToUInt16(bytes, 5));
        Assert.Equal(className, bytes[7..(7 + className.Length)]);

        // Its _characterKey is recorded as a 4-byte scalar (kind 0) holding
        // the dragon's charKey — the element content must match the charKey.
        byte[] characterKey =
        [
            .. BitConverter.GetBytes((ushort)"_characterKey".Length), .. "_characterKey"u8,
            0, .. BitConverter.GetBytes((ushort)4), .. BitConverter.GetBytes(MountCatalog.DragonCharacterKey),
        ];
        Assert.True(bytes.AsSpan().IndexOf(characterKey) >= 0,
            "template has no _characterKey = DragonCharacterKey record");
    }
}
