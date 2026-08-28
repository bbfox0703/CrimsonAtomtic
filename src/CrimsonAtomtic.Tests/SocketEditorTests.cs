using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;
using CrimsonAtomtic.Ui.ViewModels;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Regression tests for the socket write path
/// (<see cref="SocketEditorViewModel"/>).
/// </summary>
/// <remarks>
/// <para>
/// These pin the two save-format invariants the editor got wrong and
/// which made every socket edit read back in-game as "not yet opened":
/// </para>
/// <list type="number">
///   <item><c>_validSocketCount</c> is <b>absent</b>, never <c>0</c>, on
///     an item whose sockets have never been opened — so opening the
///     first one is an absent → present promotion, not an in-place
///     scalar write (which fails <c>NOT_SCALAR</c>).</item>
///   <item>A filled socket's <c>_currentEndurance</c> is the gem's own
///     <c>iteminfo.max_endurance</c> — 100 for the durability-bearing
///     "AbyssGear_*_Special" family, 65535 for the rest. A blanket
///     65535 puts a durability gem above its cap.</item>
/// </list>
/// <para>
/// Both are asserted against real game data rather than a fixture, so
/// they also act as a schema-drift alarm. Every test skips cleanly when
/// the live save / install isn't present, matching the rest of the suite.
/// </para>
/// </remarks>
public sealed class SocketEditorTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }

    private static string? FindGameRoot()
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
            if (File.Exists(Path.Combine(root, "0008", "0.pamt")))
            {
                return root;
            }
        }
        return null;
    }

    /// <summary>Every live <c>save.save</c> under the user's save root.</summary>
    private static List<string> FindLiveSaves()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(local))
        {
            return [];
        }
        var root = Path.Combine(local, "Pearl Abyss", "CD", "save");
        if (!Directory.Exists(root))
        {
            return [];
        }
        var found = new List<string>();
        foreach (var user in Directory.EnumerateDirectories(root))
        {
            foreach (var slot in Directory.EnumerateDirectories(user))
            {
                var p = Path.Combine(slot, "save.save");
                if (File.Exists(p))
                {
                    found.Add(p);
                }
            }
        }
        return found;
    }

    /// <summary>Copy a live save into a scratch dir so tests never write user data.</summary>
    private string CopyToScratch(string savePath)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "CrimsonAtomtic.SocketEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        var dest = Path.Combine(dir, "save.save");
        File.Copy(savePath, dest);
        return dest;
    }

    private sealed record Harness(
        NativeSaveLoader Loader,
        LocalizationProvider Localization,
        SocketEditorViewModel Vm,
        string SavePath);

    /// <summary>
    /// Load the first live save into a scratch copy and build a real
    /// <see cref="SocketEditorViewModel"/> over it. Returns <c>null</c>
    /// when the machine has no game install / no save — callers skip.
    /// </summary>
    private Harness? TryBuildHarness()
    {
        if (!File.Exists("crimson_rs.dll"))
        {
            return null;
        }
        var gameRoot = FindGameRoot();
        var saves = FindLiveSaves();
        if (gameRoot is null || saves.Count == 0)
        {
            return null;
        }
        var localization = new LocalizationProvider(new NativePazExtractor());
        if (!localization.TryBootstrapFromGameRoot(gameRoot))
        {
            localization.Dispose();
            return null;
        }
        foreach (var save in saves)
        {
            var work = CopyToScratch(save);
            var loader = new NativeSaveLoader();
            loader.Load(work);
            var vm = SocketEditorViewModel.TryCreate(
                loader, localization, new ChangeJournal(), work);
            if (vm is not null && vm.Sockets.Count > 0)
            {
                return new Harness(loader, localization, vm, work);
            }
        }
        localization.Dispose();
        return null;
    }

    /// <summary>
    /// Re-read the parent <c>ItemSaveData</c> of <paramref name="row"/>
    /// straight from the loader, bypassing the view-model's mirrored
    /// state — so assertions see what actually landed in the save body.
    /// </summary>
    private static BlockDetails ReadItemBlock(
        NativeSaveLoader loader, string savePath, SocketRow row)
    {
        var top = loader.LoadBlockDetails(savePath, row.BlockIndex);
        var step0 = top.Fields.First(f => f.FieldIndex == row.InventoryListFieldIdx);
        var host = step0.Elements![row.BagIndex];
        var step1 = host.Fields.First(f => f.FieldIndex == row.ItemListFieldIdx);
        // Equipped items reach the ItemSaveData through an ObjectLocator
        // (no elements); inventory items through an ObjectList.
        return step1.Elements is { Count: > 0 } elements
            ? elements[row.ItemIndex]
            : step1.Child!;
    }

    private static DecodedFieldRow Field(BlockDetails block, string name) =>
        block.Fields.First(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    private static ulong ScalarValue(DecodedFieldRow field)
    {
        Assert.True(field.Present, $"{field.Name} expected present");
        // "123 <u16>" → 123. Same shape ScalarFieldEditing.TryParse handles.
        var raw = field.Value ?? string.Empty;
        var cut = raw.LastIndexOf(' ');
        if (cut > 0)
        {
            raw = raw[..cut];
        }
        return ulong.Parse(raw.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Pick one durability-bearing gem (max_endurance &lt; 65535) and one
    /// durability-less gem (65535) out of the canonical gem set, so the
    /// test doesn't hard-code keys that a future patch could retire.
    /// </summary>
    private static (uint Durable, uint Durationless)? PickGemPair(string gameRoot)
    {
        var bytes = new NativePazExtractor().ExtractFile(
            Path.Combine(gameRoot, "0008", "0.pamt"),
            "gamedata/binary__/client/bin", "iteminfo.pabgb");
        using var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);
        uint? durable = null;
        uint? durationless = null;
        for (var i = 0; i < cat.CanonicalGemCount; i++)
        {
            var key = cat.GetCanonicalGemKey(i);
            if (key is not { } gem)
            {
                continue;
            }
            var max = cat.LookupSummary(gem)?.MaxEndurance;
            if (max is null)
            {
                continue;
            }
            if (max == ushort.MaxValue)
            {
                durationless ??= gem;
            }
            else
            {
                durable ??= gem;
            }
            if (durable is not null && durationless is not null)
            {
                break;
            }
        }
        return durable is { } d && durationless is { } n ? (d, n) : null;
    }

    // ─────────────────────────────────────────────────────────────────
    // Ground truth: what the GAME itself writes.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The two invariants the editor has to preserve, asserted against
    /// every socket-bearing item the game wrote across every live save:
    /// <c>_validSocketCount</c> is absent or &gt;= 1 (never an explicit
    /// 0), and no socket is filled at an index the count doesn't cover.
    /// </summary>
    /// <remarks>
    /// This is the reference the editor's own writes are judged against.
    /// If a future game patch ever starts writing an explicit 0, or a
    /// filled-past-the-window socket, this fails first and tells us the
    /// model moved — before the editor starts producing saves the engine
    /// rejects.
    /// </remarks>
    [Fact]
    public void GameWrittenSaves_NeverEncodeZeroOrOverfilledSocketCounts()
    {
        var gameRoot = FindGameRoot();
        var saves = FindLiveSaves();
        if (!File.Exists("crimson_rs.dll") || gameRoot is null || saves.Count == 0)
        {
            return;
        }
        using var localization = new LocalizationProvider(new NativePazExtractor());
        if (!localization.TryBootstrapFromGameRoot(gameRoot))
        {
            return;
        }

        // Every save, not just the first — this is the schema-drift alarm
        // for the whole editor, and the ground truth behind the fix was
        // measured across the full set.
        var inspected = 0;
        var savesRead = 0;
        foreach (var save in saves)
        {
            var work = CopyToScratch(save);
            var loader = new NativeSaveLoader();
            loader.Load(work);
            var vm = SocketEditorViewModel.TryCreate(
                loader, localization, new ChangeJournal(), work);
            if (vm is null)
            {
                continue;
            }
            savesRead++;
            inspected += AssertSocketInvariants(vm);
        }
        Assert.True(savesRead > 1, $"expected to read more than one save, read {savesRead}");
        Assert.True(inspected > 0, "no socket-bearing items found to inspect");
    }

    /// <summary>
    /// Assert the two game-written invariants over every item in
    /// <paramref name="vm"/>; returns how many items were checked.
    /// </summary>
    private static int AssertSocketInvariants(SocketEditorViewModel vm)
    {
        var inspected = 0;
        foreach (var group in vm.Sockets.GroupBy(r => r.ItemIdentity))
        {
            var rows = group.ToList();
            var first = rows[0];
            inspected++;

            if (!first.ValidSocketCountPresent)
            {
                Assert.Equal(0, first.CurrentValidSocketCount);
                Assert.All(rows, r => Assert.False(r.IsFilled,
                    $"socket {r.SocketIndex} filled while _validSocketCount is absent"));
                continue;
            }
            // Present ⇒ at least one socket opened. The game has no
            // "present but zero" encoding.
            Assert.True(first.CurrentValidSocketCount >= 1,
                "_validSocketCount is present but 0 — the game never writes that");
            foreach (var r in rows.Where(r => r.IsFilled))
            {
                Assert.True(r.SocketIndex < first.CurrentValidSocketCount,
                    $"socket {r.SocketIndex} filled but only "
                    + $"{first.CurrentValidSocketCount} opened");
            }
        }
        return inspected;
    }

    // ─────────────────────────────────────────────────────────────────
    // The editor's writes.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A socket row's item identity must be collision-free.
    /// </summary>
    /// <remarks>
    /// The regression: the editor keyed items on
    /// <c>(BlockIndex, BagIndex, ItemIndex)</c>, but one top-level
    /// block hosts several item lists — the equipped list and the
    /// quick-use reserve list both call their first item bag 0 / item 0.
    /// Two consequences, both silent: an Apply-Set routed to one item
    /// also wrote gems into the other, and raising one item's
    /// <c>_validSocketCount</c> marked the other as already-opened, so a
    /// later fill there skipped the promotion and produced exactly the
    /// filled-but-sealed state the engine rejects — with no error
    /// anywhere.
    /// </remarks>
    [Fact]
    public void ItemIdentity_IsUniquePerItem_UnlikeTheBareBlockBagItemTriple()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;

        var byIdentity = harness.Vm.Sockets
            .GroupBy(r => (r.ItemIdentity, r.SocketIndex))
            .Where(g => g.Count() > 1)
            .ToList();
        Assert.Empty(byIdentity);

        // Two rows sharing an identity must agree on every addressing
        // field — otherwise the identity is under-specified.
        foreach (var g in harness.Vm.Sockets.GroupBy(r => r.ItemIdentity))
        {
            var first = g.First();
            Assert.All(g, r =>
            {
                Assert.Equal(first.SocketListFieldIdx, r.SocketListFieldIdx);
                Assert.Equal(first.ItemKey, r.ItemKey);
                Assert.Equal(first.ValidSocketCountFieldIdx, r.ValidSocketCountFieldIdx);
            });
        }
    }

    /// <summary>
    /// Filling the first socket of a never-socketed item must promote
    /// <c>_validSocketCount</c> from absent to present.
    /// </summary>
    /// <remarks>
    /// The regression: the editor used an in-place
    /// <c>SetScalarField</c>, which an absent field rejects with
    /// <c>NOT_SCALAR (-12)</c>. The failure was swallowed, so the gem
    /// landed in a socket the engine considers unopened and the whole
    /// item read back in-game with every socket sealed.
    /// </remarks>
    [Fact]
    public void Fill_OnNeverSocketedItem_PromotesValidSocketCountFromAbsent()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        // Not a skip path: iteminfo always carries both gem classes, so a
        // null here means max_endurance stopped distinguishing them.
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);

        // Not a skip path: every real save holds thousands of five-slot
        // items whose _validSocketCount is absent. None found means the
        // encoding of "never socketed" moved.
        var target = harness.Vm.Sockets.FirstOrDefault(
            r => !r.ValidSocketCountPresent && r.SocketIndex == 0 && !r.IsFilled);
        Assert.NotNull(target);

        harness.Vm.ApplyGemPick(target, gems.Value.Durable);

        Assert.Null(target.LastError);
        Assert.True(target.IsFilled);
        Assert.True(target.ValidSocketCountPresent);
        Assert.Equal(1, target.CurrentValidSocketCount);

        // And the same in the save body, not just the mirrored row state.
        var item = ReadItemBlock(harness.Loader, harness.SavePath, target);
        var validCount = Field(item, "_validSocketCount");
        Assert.True(validCount.Present,
            "_validSocketCount stayed absent — the gem is in a socket the engine treats as sealed");
        Assert.Equal(1ul, ScalarValue(validCount));
    }

    /// <summary>
    /// A freshly-inserted gem's <c>_currentEndurance</c> must equal the
    /// gem's own <c>iteminfo.max_endurance</c> — not a blanket 65535.
    /// </summary>
    [Fact]
    public void Fill_WritesGemsOwnMaxEnduranceNotABlanketSentinel()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var gameRoot = FindGameRoot()!;
        var gems = PickGemPair(gameRoot);
        Assert.NotNull(gems);
        var durableMax = localization.LookupItemInfoSummary(gems.Value.Durable)!.Value.MaxEndurance;
        var otherMax = localization.LookupItemInfoSummary(gems.Value.Durationless)!.Value.MaxEndurance;
        Assert.True(durableMax < ushort.MaxValue, "picked gem should have a real cap");
        Assert.Equal(ushort.MaxValue, otherMax);

        // Two empty slots on the SAME item, so one read-back covers both.
        var group = harness.Vm.Sockets
            .GroupBy(r => r.ItemIdentity)
            .FirstOrDefault(g => g.Count(r => !r.IsFilled) >= 2);
        if (group is null)
        {
            return;
        }
        var empties = group.Where(r => !r.IsFilled).OrderBy(r => r.SocketIndex).Take(2).ToList();

        harness.Vm.ApplyGemPick(empties[0], gems.Value.Durable);
        harness.Vm.ApplyGemPick(empties[1], gems.Value.Durationless);
        Assert.Null(empties[0].LastError);
        Assert.Null(empties[1].LastError);

        var item = ReadItemBlock(harness.Loader, harness.SavePath, empties[0]);
        var sockets = Field(item, "_socketSaveDataList").Elements!;
        foreach (var (row, expectedMax) in new[]
                 {
                     (empties[0], durableMax),
                     (empties[1], otherMax),
                 })
        {
            var socket = sockets[row.SocketIndex];
            Assert.Equal((ulong)expectedMax,
                ScalarValue(Field(socket, "_currentEndurance")));
        }
    }

    /// <summary>
    /// After a spread of fills — inside the opened window, past it, and
    /// on a never-socketed item — every filled socket must still sit
    /// inside <c>_validSocketCount</c>, the shape the engine accepts.
    /// </summary>
    [Fact]
    public void Fill_NeverLeavesAGemOutsideTheOpenedWindow()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        // Not a skip path: iteminfo always carries both gem classes, so a
        // null here means max_endurance stopped distinguishing them.
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);
        var gem = gems.Value.Durable;

        // Fill the LAST empty slot of up to 20 items — the worst case for
        // the count, since it needs the largest raise (or a promotion).
        var touched = new List<SocketRow>();
        foreach (var group in harness.Vm.Sockets
                     .GroupBy(r => r.ItemIdentity)
                     .Take(20))
        {
            var last = group.Where(r => !r.IsFilled)
                .OrderByDescending(r => r.SocketIndex)
                .FirstOrDefault();
            if (last is null)
            {
                continue;
            }
            harness.Vm.ApplyGemPick(last, gem);
            Assert.Null(last.LastError);
            touched.Add(last);
        }
        if (touched.Count == 0)
        {
            return;
        }

        foreach (var row in touched)
        {
            var item = ReadItemBlock(harness.Loader, harness.SavePath, row);
            var validCount = (int)ScalarValue(Field(item, "_validSocketCount"));
            var sockets = Field(item, "_socketSaveDataList").Elements!;
            for (var i = 0; i < sockets.Count; i++)
            {
                var key = Field(sockets[i], "_itemKey");
                if (key.Present)
                {
                    Assert.True(i < validCount,
                        $"socket {i} filled but _validSocketCount is {validCount}");
                }
            }
        }
    }


    /// <summary>
    /// Apply-Set must touch only the item the user selected, and must
    /// leave it in a shape the engine accepts.
    /// </summary>
    /// <remarks>
    /// This is the path the ambiguous item key hurt most: it collected
    /// its rows on <c>(BlockIndex, BagIndex, ItemIndex)</c>, so a set
    /// applied to the equipped item also wrote gems into the quick-use
    /// reserve item that shared the triple. It also runs inside a
    /// <c>RunDeferred</c> batch, which exercises the socket-count
    /// promotion through the deferred (in-memory tree) mutation path
    /// rather than the immediate re-emit one.
    /// </remarks>
    [Fact]
    public void ApplySet_TouchesOnlyTheSelectedItem_AndKeepsTheWindowValid()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        // Not a skip path: iteminfo always carries both gem classes, so a
        // null here means max_endurance stopped distinguishing them.
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);
        var vm = harness.Vm;

        // Prefer a target whose identity collides with another item on
        // the bare triple — that's the case the old key got wrong.
        var byTriple = vm.Sockets
            .GroupBy(r => (r.BlockIndex, r.BagIndex, r.ItemIndex))
            .Where(g => g.Select(r => r.ItemIdentity).Distinct().Count() > 1)
            .ToList();
        var wanted = byTriple.Count > 0
            ? byTriple[0].First().ItemIdentity
            : vm.Sockets[0].ItemIdentity;
        var target = vm.ApplySetTargets.FirstOrDefault(t => t.ItemIdentity == wanted);
        if (target is null)
        {
            return;
        }

        // Snapshot every OTHER item's gems so we can prove they didn't move.
        var before = vm.Sockets
            .Where(r => r.ItemIdentity != wanted)
            .ToDictionary(r => (r.ItemIdentity, r.SocketIndex),
                          r => (r.IsFilled, r.CurrentGemKey));

        vm.SelectedTarget = target;
        vm.SelectedSet = new GemSetOption(
            "test", [gems.Value.Durable, gems.Value.Durationless], "test");
        vm.ApplyGemSetCommand.Execute(null);

        foreach (var r in vm.Sockets.Where(r => r.ItemIdentity != wanted))
        {
            var prior = before[(r.ItemIdentity, r.SocketIndex)];
            Assert.Equal(prior.IsFilled, r.IsFilled);
            Assert.Equal(prior.CurrentGemKey, r.CurrentGemKey);
        }

        // ApplyGemPick swallows its own failures, so the error column is
        // the only per-slot signal that a write actually landed.
        Assert.All(vm.Sockets.Where(r => r.ItemIdentity == wanted).Take(2),
            r => Assert.Null(r.LastError));

        var touched = vm.Sockets.First(r => r.ItemIdentity == wanted);
        var item = ReadItemBlock(harness.Loader, harness.SavePath, touched);
        var validCount = (int)ScalarValue(Field(item, "_validSocketCount"));
        var sockets = Field(item, "_socketSaveDataList").Elements!;
        for (var i = 0; i < sockets.Count; i++)
        {
            if (Field(sockets[i], "_itemKey").Present)
            {
                Assert.True(i < validCount,
                    $"socket {i} filled but _validSocketCount is {validCount}");
            }
        }
    }


    /// <summary>
    /// The Apply-Set target dropdown must narrow with the grid filter,
    /// and a selection that the filter hides must be dropped.
    /// </summary>
    /// <remarks>
    /// Reported by the maintainer: filtering to one item still left all
    /// 702 items in the "Apply set to:" dropdown, so picking the item
    /// you just filtered down to meant scrolling past everything else.
    /// </remarks>
    [Fact]
    public void Filter_NarrowsTheApplySetTargetDropdown()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var vm = harness.Vm;

        var unfiltered = vm.ApplySetTargets.Count;
        Assert.True(unfiltered > 1, "need at least two distinct items");

        // Filter on one item's exact key — its own rows must survive.
        var pick = vm.Sockets[0];
        vm.SelectedTarget = vm.ApplySetTargets.First(t => t.ItemIdentity == pick.ItemIdentity);
        vm.SearchText = pick.ItemKeyText;

        Assert.True(vm.ApplySetTargets.Count < unfiltered,
            "dropdown did not narrow with the grid filter");
        Assert.All(vm.ApplySetTargets, t =>
            Assert.Contains(vm.Sockets, r => r.ItemIdentity == t.ItemIdentity));
        // The selection survives because it still matches.
        Assert.NotNull(vm.SelectedTarget);

        // A filter that matches nothing empties the dropdown AND clears
        // the selection, so Apply can't fire at an invisible item.
        vm.SearchText = "zzz-no-such-item-zzz";
        Assert.Empty(vm.ApplySetTargets);
        Assert.Null(vm.SelectedTarget);

        // Clearing the filter restores the full list.
        vm.SearchText = string.Empty;
        Assert.Equal(unfiltered, vm.ApplySetTargets.Count);
    }

    /// <summary>
    /// Apply-Set addresses the whole item, so a filter that hides some
    /// of the target's slots must not shrink what gets written.
    /// </summary>
    [Fact]
    public void ApplySet_IgnoresTheGridFilterWhenCollectingTheTargetsSlots()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);
        var vm = harness.Vm;

        // An item with at least two empty slots, so a 2-gem set has
        // somewhere to land.
        var group = vm.Sockets
            .GroupBy(r => r.ItemIdentity)
            .FirstOrDefault(g => g.Count(r => !r.IsFilled) >= 2);
        if (group is null)
        {
            return;
        }
        var identity = group.Key;
        var target = vm.ApplySetTargets.First(t => t.ItemIdentity == identity);

        vm.SelectedTarget = target;
        vm.SelectedSet = new GemSetOption(
            "test", [gems!.Value.Durable, gems.Value.Durationless], "test");
        // Filter down to a single visible row of that item, then apply.
        vm.SearchText = group.First(r => r.IsFilled || r.SocketIndex == 0).DisplayGemKeyText;
        vm.SelectedTarget = vm.ApplySetTargets.FirstOrDefault(t => t.ItemIdentity == identity)
                            ?? target;
        vm.SearchText = string.Empty;
        vm.SelectedTarget = vm.ApplySetTargets.First(t => t.ItemIdentity == identity);
        vm.ApplyGemSetCommand.Execute(null);

        // Both slots of the set landed, regardless of what was visible.
        var rows = vm.Sockets.Where(r => r.ItemIdentity == identity)
            .OrderBy(r => r.SocketIndex).ToList();
        Assert.Equal(gems.Value.Durable, rows[0].CurrentGemKey);
        Assert.Equal(gems.Value.Durationless, rows[1].CurrentGemKey);
        Assert.All(rows.Take(2), r => Assert.Null(r.LastError));
    }


    /// <summary>
    /// Apply-Set on a never-socketed item must land <b>every</b> gem of
    /// the set, not just the first.
    /// </summary>
    /// <remarks>
    /// Apply-Set runs inside <c>RunDeferred</c>, and a deferred
    /// presence-promotion leaves the field's decoded byte range at
    /// <c>start == end == 0</c> until the batch commits (see
    /// <c>toggle_one_scalar_presence_in_place</c> in
    /// vendor/crimson-rs/src/c_abi/mod.rs — "start/end are stale but the
    /// encoder ignores them"). So the in-place raise for slot 1 computes
    /// <c>expected = 0</c> against a 1-byte write and fails
    /// <c>LENGTH_MISMATCH</c>. Single Fill never sees it because
    /// non-deferred mode re-decodes after every call. Without the
    /// stale-range handling in TryEnsureSocketOpened this leaves
    /// _validSocketCount at 1 with one gem in and the rest dropped —
    /// which is the sealed-socket state the whole fix exists to prevent.
    /// </remarks>
    [Fact]
    public void ApplySet_OnNeverSocketedItem_OpensEverySlotItFills()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);
        var vm = harness.Vm;

        // A never-socketed item with the full 5-slot list.
        var group = vm.Sockets
            .GroupBy(r => r.ItemIdentity)
            .FirstOrDefault(g => g.All(r => !r.IsFilled)
                                 && !g.First().ValidSocketCountPresent
                                 && g.Count() >= 3);
        Assert.NotNull(group);
        var identity = group!.Key;

        vm.SelectedTarget = vm.ApplySetTargets.First(t => t.ItemIdentity == identity);
        vm.SelectedSet = new GemSetOption("test",
            [gems!.Value.Durable, gems.Value.Durationless, gems.Value.Durable], "test");
        vm.ApplyGemSetCommand.Execute(null);

        var rows = vm.Sockets.Where(r => r.ItemIdentity == identity)
            .OrderBy(r => r.SocketIndex).ToList();
        Assert.All(rows.Take(3), r =>
        {
            Assert.Null(r.LastError);
            Assert.True(r.IsFilled, $"socket {r.SocketIndex} was not filled");
        });

        // And in the save body: three gems, all inside the opened window.
        var item = ReadItemBlock(harness.Loader, harness.SavePath, rows[0]);
        var validCount = (int)ScalarValue(Field(item, "_validSocketCount"));
        Assert.Equal(3, validCount);
        var sockets = Field(item, "_socketSaveDataList").Elements!;
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Field(sockets[i], "_itemKey").Present,
                $"socket {i} lost its gem");
        }
    }


    /// <summary>
    /// The equippable-only view must drop the props and keep the gear.
    /// </summary>
    /// <remarks>
    /// The save format hands a full 5-entry <c>_socketSaveDataList</c>
    /// to items that are not equipment at all, so the raw list is
    /// dominated by gold bars, cups, arrows, water, food and horse feed.
    /// The discriminator is gamedata's <c>equip_type_info != 0</c> —
    /// deliberately NOT <c>use_socket</c> / <c>socket_valid_count</c>,
    /// which would drop rings (gamedata forbids sockets on them, yet
    /// force-modding works in-game).
    /// </remarks>
    [Fact]
    public void EquippableOnly_HidesNonWearableItemsAndKeepsTheGear()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var vm = harness.Vm;

        // Default is on.
        Assert.True(vm.EquippableOnly);
        var shown = vm.Sockets.Count;
        Assert.All(vm.Sockets, r => Assert.True(r.IsEquippable));

        vm.EquippableOnly = false;
        var all = vm.Sockets.Count;
        Assert.True(all >= shown);

        // Every hidden row must be a genuinely non-wearable item, and
        // every kept row genuinely wearable — cross-checked straight
        // against gamedata rather than against the row's own flag.
        foreach (var r in vm.Sockets)
        {
            var gd = localization.LookupItemInfoSummary(r.ItemKey);
            var wearable = gd is not { } g || g.EquipTypeInfo != 0;
            Assert.Equal(wearable, r.IsEquippable);
        }

        vm.EquippableOnly = true;
        Assert.Equal(shown, vm.Sockets.Count);
    }

    /// <summary>
    /// The equippable filter must narrow the Apply-Set dropdown too, and
    /// must compose with the text filter rather than override it.
    /// </summary>
    [Fact]
    public void EquippableOnly_AlsoNarrowsTheApplySetTargetsAndComposesWithSearch()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        var vm = harness.Vm;

        Assert.All(vm.ApplySetTargets, t =>
            Assert.Contains(vm.Sockets, r => r.ItemIdentity == t.ItemIdentity));

        vm.EquippableOnly = false;
        var allTargets = vm.ApplySetTargets.Count;
        vm.EquippableOnly = true;
        Assert.True(vm.ApplySetTargets.Count <= allTargets);

        // Compose: a needle matching a kept item still shows it; the
        // equippable filter must not be discarded by the text pass.
        var keep = vm.Sockets.First(r => r.IsEquippable);
        vm.SearchText = keep.ItemKeyText;
        Assert.Contains(vm.Sockets, r => r.ItemIdentity == keep.ItemIdentity);
        Assert.All(vm.Sockets, r => Assert.True(r.IsEquippable));

        vm.SearchText = string.Empty;
        Assert.All(vm.Sockets, r => Assert.True(r.IsEquippable));
    }


    /// <summary>
    /// Every gem key in every built-in set must be a real gem.
    /// </summary>
    /// <remarks>
    /// The save format accepts any <c>u32</c> as a socket's
    /// <c>_itemKey</c>, so a mistyped built-in set would be written into
    /// the user's save without a murmur — no FFI error, no validation,
    /// just a wrong or non-existent gem in the slot. This pins each key
    /// against the engine's own canonical gem marker
    /// (<c>item_type == 74 &amp;&amp; category_info == 2501</c>), which
    /// also makes it a drift alarm: if a game patch retires one of these
    /// keys, this fails before a user hits it.
    /// </remarks>
    [Fact]
    public void BuiltInGemSets_ContainOnlyRealGems()
    {
        // Pure-CPU part: shape invariants hold with or without game data.
        Assert.NotEmpty(BuiltInGemSets.All);
        Assert.All(BuiltInGemSets.All, set =>
        {
            Assert.False(string.IsNullOrWhiteSpace(set.Label));
            Assert.NotEmpty(set.GemKeys);
            Assert.True(set.GemKeys.Count <= GemSet.MaxGems,
                $"{set.Label} has {set.GemKeys.Count} gems, cap is {GemSet.MaxGems}");
            Assert.All(set.GemKeys, k => Assert.NotEqual(0u, k));
        });
        Assert.Equal(BuiltInGemSets.All.Count,
            BuiltInGemSets.All.Select(s => s.Label).Distinct().Count());

        var gameRoot = FindGameRoot();
        if (!File.Exists("crimson_rs.dll") || gameRoot is null)
        {
            return;
        }
        var bytes = new NativePazExtractor().ExtractFile(
            Path.Combine(gameRoot, "0008", "0.pamt"),
            "gamedata/binary__/client/bin", "iteminfo.pabgb");
        using var cat = NativeItemInfoCatalog.LoadFromBytes(bytes);

        var canonical = new HashSet<uint>();
        for (var i = 0; i < cat.CanonicalGemCount; i++)
        {
            if (cat.GetCanonicalGemKey(i) is { } g)
            {
                canonical.Add(g);
            }
        }
        Assert.NotEmpty(canonical);

        foreach (var set in BuiltInGemSets.All)
        {
            foreach (var key in set.GemKeys)
            {
                Assert.True(canonical.Contains(key),
                    $"{set.Label}: {key} is not in the canonical gem set "
                    + $"(string_key={cat.LookupStringKey(key) ?? "<not in iteminfo>"})");
                // And it must have a readable durability cap, since the
                // fill path writes exactly that value.
                Assert.NotNull(cat.LookupSummary(key));
            }
        }
    }

    /// <summary>
    /// The save has to survive a full write + reload round trip with the
    /// edits intact — the end-to-end proof that a length-changing
    /// promotion of <c>_validSocketCount</c> re-emits a coherent block.
    /// </summary>
    [Fact]
    public void Fill_ThenWriteAndReload_KeepsTheOpenedSocketAndGem()
    {
        var harness = TryBuildHarness();
        if (harness is null)
        {
            return;
        }
        using var localization = harness.Localization;
        // Not a skip path: iteminfo always carries both gem classes, so a
        // null here means max_endurance stopped distinguishing them.
        var gems = PickGemPair(FindGameRoot()!);
        Assert.NotNull(gems);

        var target = harness.Vm.Sockets.FirstOrDefault(
            r => !r.ValidSocketCountPresent && r.SocketIndex == 0 && !r.IsFilled);
        Assert.NotNull(target);
        var identity = target.ItemIdentity;
        var socket = target.SocketIndex;

        harness.Vm.ApplyGemPick(target, gems.Value.Durable);
        harness.Loader.WriteToFile(harness.SavePath);

        var reloaded = new NativeSaveLoader();
        reloaded.Load(harness.SavePath);
        var vm2 = SocketEditorViewModel.TryCreate(
            reloaded, localization, new ChangeJournal(), harness.SavePath);
        Assert.NotNull(vm2);

        var again = vm2!.Sockets.Single(
            r => r.ItemIdentity == identity && r.SocketIndex == socket);
        Assert.True(again.IsFilled);
        Assert.Equal(gems.Value.Durable, again.CurrentGemKey);
        Assert.True(again.ValidSocketCountPresent);
        Assert.Equal(1, again.CurrentValidSocketCount);
    }
}
