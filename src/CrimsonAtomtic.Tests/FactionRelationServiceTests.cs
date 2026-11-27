using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Edit Faction Relations against a copy of the newest save: every kind of
/// change — retype a row, turn a Neutral row into War, add rows to a faction
/// that had none, remove rows back to the group default — lands in both
/// directions and reads back from the written file.
/// </summary>
public sealed class FactionRelationServiceTests(ITestOutputHelper output)
{
    [Fact]
    public void Apply_AllFourShapes_RoundTripBothWays()
    {
        if (!File.Exists("crimson_rs.dll") || LiveSaves.Newest() is not { } save)
        {
            return;
        }
        var dir = Directory.CreateTempSubdirectory("crimson-rel-");
        try
        {
            using var loader = new NativeSaveLoader();
            var blocks = loader.Load(save).Blocks;
            var service = new FactionRelationService(loader);
            Assert.Equal(1000000u, service.PlayerFaction(save, blocks));
            var before = service.Scan(save, blocks);
            Assert.True(before.Count > 100, $"expected >100 factions, got {before.Count}");
            Assert.DoesNotContain(before, e => e.FactionKey == 1000000);

            static bool Typed(FactionRelationEntry e) =>
                e.PlayerToFaction is FactionRelation.War or FactionRelation.Agreement or FactionRelation.BloodAlly
                && !e.IsMismatched;
            var typed = before.Where(Typed).ToList();
            var neutral = before.FirstOrDefault(e => e is { PlayerToFaction: FactionRelation.Neutral, IsMismatched: false });
            var none = before.FirstOrDefault(e => e is { PlayerToFaction: FactionRelation.GroupDefault, FactionToPlayer: FactionRelation.GroupDefault });
            if (typed.Count < 2 || neutral is null || none is null)
            {
                return; // this save lacks one of the shapes
            }
            var retype = typed[0];
            var remove = typed[1];
            var newType = retype.PlayerToFaction == FactionRelation.Agreement ? FactionRelation.BloodAlly : FactionRelation.Agreement;
            List<FactionRelationChange> changes =
            [
                new(retype.FactionKey, newType),
                new(neutral.FactionKey, FactionRelation.War),
                new(none.FactionKey, FactionRelation.BloodAlly),
                new(remove.FactionKey, FactionRelation.GroupDefault),
            ];
            output.WriteLine(string.Join("; ", changes.Select(c =>
                $"{c.FactionKey}: {before.Single(e => e.FactionKey == c.FactionKey).PlayerToFaction} -> {c.Relation}")));
            var result = service.Apply(save, blocks, changes);
            Assert.Null(result.Error);
            Assert.Equal(4, result.Applied);

            var outPath = Path.Combine(dir.FullName, "save.save");
            loader.WriteToFile(outPath);
            using var reread = new NativeSaveLoader();
            var blocks2 = reread.Load(outPath).Blocks;
            var after = new FactionRelationService(reread).Scan(outPath, blocks2).ToDictionary(e => e.FactionKey);
            foreach (var c in changes)
            {
                var e = after[c.FactionKey];
                Assert.Equal(c.Relation, e.PlayerToFaction);
                Assert.Equal(c.Relation, e.FactionToPlayer);
            }
            // Everything else is untouched.
            foreach (var b in before.Where(b => changes.All(c => c.FactionKey != b.FactionKey)))
            {
                Assert.Equal(b, after[b.FactionKey]);
            }
            var block = blocks2.Single(b => b.ClassName == "FactionSaveData").Index;
            Assert.Empty(reread.LoadBlockDetails(outPath, block).UndecodedRanges);
            AssertNewRowShape(reread, outPath, block, none.FactionKey);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>A row added from scratch carries the target, the type and the permanent due date.</summary>
    private static void AssertNewRowShape(NativeSaveLoader loader, string path, int block, uint faction)
    {
        var details = loader.LoadBlockDetails(path, block);
        var element = details.Fields.Single(f => f.Name == "_factionElementSaveDataList").Elements!
            .Single(e => U(e, "_ownerFactionKey") == faction);
        var row = Assert.Single(element.Fields.Single(f => f.Name == "_factionRelationDataList").Elements!);
        Assert.Equal(1000000UL, U(row, "_targetFactionKey"));
        Assert.Equal((ulong)FactionRelation.BloodAlly, U(row, "_relationTypeRaw"));
        Assert.Equal((ulong)uint.MaxValue, U(row, "_dueDate"));
    }

    [Fact]
    public void RelationGroupNames_ResolveThroughTheTable()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        Assert.Equal("Graymane", loc.ResolveByFieldTypeName("FactionRelationGroupKey", 16963));
        Assert.Equal("HostileCombat", loc.ResolveByFieldTypeName("FactionRelationGroupKey", 16965));
        Assert.Equal("Monster_Common", loc.ResolveByFieldTypeName("FactionRelationGroupKey", 16967));
    }

    /// <summary>
    /// Member counts come from characterinfo's faction field and are what
    /// the in-game test hinged on: Hernand's guards follow Celeste_Hernand
    /// (66 characters), while the Roberts family faction has 2.
    /// </summary>
    [Fact]
    public void MemberCountsAndGroupNames_Live()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        Assert.Equal(66, loc.FactionMemberCount(1000131));   // Faction_Celeste_Hernand
        // dlc_01 adds 15 Greymane characters when it is installed (merged at bootstrap).
        var hasDlc = GameDataLayout.DlcNames(Path.GetDirectoryName(Path.GetDirectoryName(pamt))!).Contains("dlc_01");
        Assert.Equal(hasDlc ? 187 : 172, loc.FactionMemberCount(1000000));  // Faction_Graymane
        Assert.Equal(2, loc.FactionMemberCount(1000001));    // Faction_Roberts
        Assert.Equal(0, loc.FactionMemberCount(1000002));    // Faction_Serkis
        Assert.Equal("Hernand", loc.ResolveFactionDisplayName(1000131).English);
        Assert.Equal("Greymanes", loc.ResolveFactionDisplayName(1000000).English);

        // Factions have no names; their members' do, and that is what the
        // editor shows. The guards are what the player sees in Hernand.
        Assert.Equal("Hernandian Guard", loc.FactionMemberNames(1000131)[0].Name);
        Assert.Equal("Bandit", loc.FactionMemberNames(1000128)[0].Name);
        Assert.Empty(loc.FactionMemberNames(1000002)); // Faction_Serkis has no members
    }

    /// <summary>
    /// A faction node has no name of its own; it names a knowledge row whose
    /// title is the place the game shows (crimson-rs PR #118).
    /// </summary>
    [Fact]
    public void FactionNodePlaceNames_Live()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(Path.GetDirectoryName(Path.GetDirectoryName(pamt))));
        Assert.Equal("Hernand Castle", loc.ResolveFactionNodeName(1000044).English);   // Node_Her_HernandCastle
        Assert.Equal("Glenbright Farm", loc.ResolveFactionNodeName(1000311).English);  // Node_Her_GlenbrightFarm
        Assert.Equal("Howling Hill", loc.ResolveFactionNodeName(1000003).English);     // Node_Her_GreymaneCamp
        Assert.Null(loc.ResolveFactionNodeName(uint.MaxValue).English);
    }

    /// <summary>
    /// dlc_01's copies of factionnode / knowledge / faction / characterinfo
    /// are merged into the base handles (crimson-rs PR #119), so DLC rows
    /// resolve too. Skipped on an install without the DLC.
    /// </summary>
    [Fact]
    public void DlcRows_ResolveAfterMerge_Live()
    {
        if (!File.Exists("crimson_rs.dll")
            || LiveInstall.FindGroupPamt(GameDataLayout.GameDataGroup) is not { } pamt)
        {
            return;
        }
        var root = Path.GetDirectoryName(Path.GetDirectoryName(pamt))!;
        if (!GameDataLayout.DlcNames(root).Contains("dlc_01"))
        {
            return;
        }
        using var loc = new LocalizationProvider(new NativePazExtractor());
        Assert.True(loc.TryBootstrapFromGameRoot(root));
        Assert.Equal("Iliya Island", loc.ResolveFactionNodeName(1000566).English);     // Node_Ocean_IliyaIsland
        Assert.Equal("Faction_Satyros", loc.ResolveByFieldTypeName("FactionKey", 1000099));
        Assert.Equal(9, loc.FactionMemberCount(1000099));
    }

    private static ulong U(BlockDetails element, string field)
    {
        var f = element.Fields.Single(x => x.Name == field);
        return f.Present && ScalarFieldEditing.TryParse(f.Value, out var raw, out _)
            ? ulong.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : ulong.MaxValue - 1;
    }
}
