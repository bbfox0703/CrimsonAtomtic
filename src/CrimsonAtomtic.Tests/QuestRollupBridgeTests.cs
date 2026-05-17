using CrimsonAtomtic.RustInterop;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Smoke tests for the two curated quest-rollup bridges
/// (<see cref="NativeMainQuestChapter"/> +
/// <see cref="NativeSideQuestFaction"/>). Both are static
/// no-handle / no-file lookups so the tests skip cleanly only when
/// the C ABI DLL itself is absent.
/// </summary>
public sealed class QuestRollupBridgeTests
{
    private static bool DllPresent => File.Exists("crimson_rs.dll");

    // ── main_quest_chapter ──────────────────────────────────────────────────

    [Fact]
    public void MainQuest_EntryCount_PositiveAndReasonable()
    {
        if (!DllPresent) return;
        var count = NativeMainQuestChapter.EntryCount;
        // Vendor side advertises ~170 rows across Prologue + 12
        // chapters + Epilogue. Lower bound asserts the table is loaded;
        // upper bound catches accidental ROW append explosions.
        Assert.InRange(count, 100, 400);
    }

    [Fact]
    public void MainQuest_GetEntry_FirstRowReturnsPrologue()
    {
        if (!DllPresent) return;
        var row = NativeMainQuestChapter.GetEntry(0);
        Assert.NotNull(row);
        // Prologue rows always have an empty arc string per the
        // vendor-side contract; the chapter heading should mention
        // "Prologue" since the table is ordered by table appearance.
        Assert.Contains("Prologue", row.Value.Chapter, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, row.Value.Arc);
        Assert.False(string.IsNullOrEmpty(row.Value.Mission));
    }

    [Fact]
    public void MainQuest_GetEntry_OutOfRangeReturnsNull()
    {
        if (!DllPresent) return;
        var count = NativeMainQuestChapter.EntryCount;
        Assert.Null(NativeMainQuestChapter.GetEntry(count + 10));
    }

    [Fact]
    public void MainQuest_ChapterForArc_UnknownReturnsNull()
    {
        if (!DllPresent) return;
        Assert.Null(NativeMainQuestChapter.ChapterForArc("__not a real arc__"));
    }

    [Fact]
    public void MainQuest_RoundtripArcLookupViaTable()
    {
        if (!DllPresent) return;
        // Pull a non-Prologue row out of the table and round-trip its
        // arc through ChapterForArc — pins that both halves of the
        // bridge agree on the same row. Walks rows to find the first
        // with a non-empty arc.
        var count = NativeMainQuestChapter.EntryCount;
        (string Chapter, string Arc, string Mission)? row = null;
        for (var i = 0; i < count; i++)
        {
            var r = NativeMainQuestChapter.GetEntry(i);
            if (r is not null && !string.IsNullOrEmpty(r.Value.Arc))
            {
                row = r;
                break;
            }
        }
        Assert.NotNull(row);
        var resolved = NativeMainQuestChapter.ChapterForArc(row.Value.Arc);
        Assert.Equal(row.Value.Chapter, resolved);
    }

    // ── side_quest_faction ──────────────────────────────────────────────────

    [Fact]
    public void SideQuest_EntryCount_PositiveAndReasonable()
    {
        if (!DllPresent) return;
        var count = NativeSideQuestFaction.EntryCount;
        // Vendor side advertises ~84 quests across 22 factions.
        Assert.InRange(count, 50, 200);
    }

    [Fact]
    public void SideQuest_GetEntry_FirstRowHasQuestAndFaction()
    {
        if (!DllPresent) return;
        var row = NativeSideQuestFaction.GetEntry(0);
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty(row.Value.Quest));
        Assert.False(string.IsNullOrEmpty(row.Value.Faction));
    }

    [Fact]
    public void SideQuest_FactionForQuest_UnknownReturnsNull()
    {
        if (!DllPresent) return;
        Assert.Null(NativeSideQuestFaction.FactionForQuest("__not a real quest__"));
    }

    [Fact]
    public void SideQuest_RoundtripQuestLookupViaTable()
    {
        if (!DllPresent) return;
        var row = NativeSideQuestFaction.GetEntry(0);
        Assert.NotNull(row);
        var resolvedFaction = NativeSideQuestFaction.FactionForQuest(row.Value.Quest);
        Assert.Equal(row.Value.Faction, resolvedFaction);

        // Reverse lookup: faction → list. The original quest must
        // appear in the faction's enumerated list.
        var factionCount = NativeSideQuestFaction.QuestCountForFaction(row.Value.Faction);
        Assert.True(factionCount >= 1);
        bool found = false;
        for (var i = 0; i < factionCount; i++)
        {
            var q = NativeSideQuestFaction.QuestAtForFaction(row.Value.Faction, i);
            if (string.Equals(q, row.Value.Quest, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }
        Assert.True(found,
            $"Quest '{row.Value.Quest}' should appear in faction '{row.Value.Faction}' enumeration");
    }

    [Fact]
    public void SideQuest_UnknownFaction_ReturnsZeroAndNullEntries()
    {
        if (!DllPresent) return;
        Assert.Equal(0, NativeSideQuestFaction.QuestCountForFaction("__no such faction__"));
        Assert.Null(NativeSideQuestFaction.QuestAtForFaction("__no such faction__", 0));
    }
}
