using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the Tools → Edit Item Sockets dialog. Surfaces
/// every socket-capable item across all five container kinds (active
/// equip / reserve / inventory / mercenary equip / mercenary
/// inventory) via <see cref="ISaveLoader.ListAllItems"/>, one row
/// per socket slot (both <b>empty</b> and <b>filled</b>).
/// </summary>
/// <remarks>
/// <para>
/// v2 scope (2026-05-16): Fill / Change / Clear per-slot. Per-row
/// Change-and-Fill open a gem-filtered <see cref="ItemPickerViewModel"/>;
/// the picked <see cref="ItemPickerRow.ItemKey"/> goes into the slot
/// via the appropriate FFI:
/// <list type="bullet">
///   <item><b>Fill</b> (empty → gem): both <c>_currentEndurance</c> +
///     <c>_itemKey</c> promoted absent → present via
///     <see cref="ISaveLoader.SetScalarFieldsPresentBatch"/>. Endurance
///     is the gem's own <c>iteminfo.max_endurance</c> — see
///     <see cref="ResolveGemEndurance"/>.</item>
///   <item><b>Change</b> (filled → different gem): in-place
///     <see cref="ISaveLoader.SetScalarField"/> on <c>_itemKey</c> +
///     <c>_currentEndurance</c> reset to the new gem's max (durability
///     fix for greater gems — v1 left the old slot's worn value in
///     place).</item>
///   <item><b>Clear</b> (filled → empty): both fields demoted to
///     absent via <see cref="ISaveLoader.SetScalarFieldsPresentBatch"/>.</item>
/// </list>
/// Every Fill first makes sure the slot is actually <i>open</i> — see
/// <see cref="TryEnsureSocketOpened"/>, which raises
/// <c>_validSocketCount</c> or promotes it from absent, and aborts the
/// gem write if it can't. Within that window the dialog lets you fill
/// <i>any</i> slot up to the underlying <c>_socketSaveDataList</c>'s
/// actual capacity (the engine pre-allocates 5 slots for socket-capable
/// items) regardless of the gamedata-defined limit, so CE-bypassed
/// slots are accepted.
/// </para>
/// <para>
/// Out of scope: socket-count unlock for items that ship with
/// <c>_maxSocketCount = 0</c> (zero-record list). CRIMSON-DESERT-SAVE-EDITOR
/// hard-warned "0→positive on a zero-record list may crash" because
/// it requires length-changing the list itself — different mutation
/// surface than the in-place fill v2 uses.
/// </para>
/// </remarks>
public sealed partial class SocketEditorViewModel : ObservableObject
{
    private const string SocketListFieldName = "_socketSaveDataList";
    private const string ItemKeyFieldName = "_itemKey";

    /// <summary>
    /// Field name carrying the durability for greater (durability-
    /// bearing) gems. u16; reset to the gem's own
    /// <c>iteminfo.max_endurance</c> on every Fill / Change so a fresh
    /// gem doesn't inherit the previous slot's worn value.
    /// </summary>
    private const string EnduranceFieldName = "_currentEndurance";

    /// <summary>
    /// u8 field on the parent <c>ItemSaveData</c> capturing how many of
    /// the slot list's entries are currently "open" (usable in-game).
    /// <b>Absent</b> — not 0 — is how the game encodes "none opened
    /// yet", so opening the first socket is a presence promotion.
    /// Filling a slot whose index is &gt;= this value raises it; see
    /// <see cref="TryEnsureSocketOpened"/>.
    /// </summary>
    private const string ValidSocketCountFieldName = "_validSocketCount";

    /// <summary>
    /// Fallback "fresh gem" endurance, used only when the gem key is
    /// absent from <c>iteminfo</c> (CE-invented keys) or the iteminfo
    /// bridge isn't loaded. Normally the value comes from
    /// <see cref="ResolveGemEndurance"/>.
    /// </summary>
    /// <remarks>
    /// 65535 is <b>not</b> a generic "fresh gem" sentinel — it is
    /// simply what <c>max_endurance</c> happens to be for the
    /// durability-less gems. Every socket the game itself writes
    /// carries <c>_currentEndurance == iteminfo.max_endurance</c> of
    /// the gem: 65535 for durability-less gems, but <b>100</b> for the
    /// "AbyssGear_*_Special" family (item keys 1002862 / 1002969..
    /// 1002982). Verified across all 734 filled sockets (54 distinct
    /// gems) in four live saves — the mapping is 1:1 with zero
    /// exceptions, and no gem ever mixes 65535 with a real durability
    /// value. Writing a
    /// blanket 65535 puts a durability gem above its own cap, which the
    /// engine rejects: the whole item's socket block reads back as
    /// not-yet-opened in-game.
    /// </remarks>
    public const ushort DefaultGemEndurance = 0xFFFF;

    /// <summary>
    /// String-key prefixes that identify gem items in 1.06 iteminfo.
    /// "AbyssGear" is the engine-internal name for what's localized as
    /// "gem" in-game; gems split into stat-modifier gems
    /// (<c>Item_Stat_AbyssGear_*</c>) and skill-bestowing gems
    /// (<c>Item_Skill_AbyssGear_*</c>). 100% of CRIMSON-DESERT-SAVE-EDITOR's
    /// curated 189-entry gem list falls under one of these
    /// two prefixes in the 1.06 baseline dump.
    /// </summary>
    public static readonly IReadOnlyList<string> GemStringKeyPrefixes =
    [
        "Item_Stat_AbyssGear_",
        "Item_Skill_AbyssGear_",
    ];

    /// <summary>
    /// Non-zero while a <see cref="ISaveLoader.RunDeferred"/> batch is
    /// open. Inside one, a presence promotion leaves the promoted
    /// field's decoded byte range stale until the commit re-decodes, so
    /// <see cref="TryEnsureSocketOpened"/> has to avoid the in-place
    /// scalar setter for a field it promoted in the same batch.
    /// </summary>
    private int _deferredDepth;

    /// <summary>
    /// The loader's mutation version as of the snapshot every
    /// <see cref="SocketRow"/> was built from, kept in step with this
    /// dialog's own writes. See <see cref="IsSnapshotStale"/>.
    /// </summary>
    private ulong _snapshotVersion;

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly ChangeJournal _journal;
    private readonly string _savePath;

    /// <summary>
    /// Localization handle exposed for child dialogs (e.g. the gem
    /// picker that the Sockets editor opens via
    /// <see cref="ChangeGemRequested"/>). Held by reference — the
    /// editor doesn't take ownership.
    /// </summary>
    public LocalizationProvider Localization => _localization;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// Currently-visible socket rows. Filtered subset of
    /// <see cref="_allSockets"/> when <see cref="SearchText"/> is
    /// non-empty; otherwise the whole set. DataGrid binds to this.
    /// </summary>
    public ObservableCollection<SocketRow> Sockets { get; } = new();

    /// <summary>
    /// Full unfiltered snapshot — built once during
    /// <see cref="TryCreate"/>. <see cref="ApplyFilter"/> walks this
    /// list and republishes the matches into <see cref="Sockets"/>.
    /// Kept as <see cref="List{T}"/> (not observable) so filter passes
    /// don't fire CollectionChanged on the snapshot side.
    /// </summary>
    private readonly List<SocketRow> _allSockets = new();

    /// <summary>
    /// Live filter input — bound to a TextBox above the DataGrid. A
    /// substring match (case-insensitive) against
    /// <see cref="SocketRow.BagLabel"/>,
    /// <see cref="SocketRow.ItemNameEnglish"/>,
    /// <see cref="SocketRow.ItemNameSecondary"/>,
    /// <see cref="SocketRow.ItemKeyText"/>,
    /// <see cref="SocketRow.DisplayGemName"/> and
    /// <see cref="SocketRow.DisplayGemKeyText"/> filters the visible
    /// rows down. Empty / whitespace = show everything.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterCountText))]
    private string? _searchText;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    /// <summary>
    /// When true (the default), only items that can actually be worn
    /// are listed — see <see cref="SocketRow.IsEquippable"/>.
    /// </summary>
    /// <remarks>
    /// The save hands a full 5-entry <c>_socketSaveDataList</c> to a
    /// great many items that are not equipment at all, so the raw list
    /// is dominated by gold bars, cups, arrows, water, carrots, cooking
    /// oil, horse feed and other props — on the maintainer's slot101,
    /// 789 of 1,543 socket-bearing rows. None of them can carry a gem
    /// in any meaningful sense, and they bury the gear you came for.
    /// <para>
    /// Kept as a toggle rather than a hard rule, because "gamedata says
    /// no but the engine accepts it" is a real and supported case here
    /// — rings can't legitimately be socketed and force-modding them
    /// works fine. The filter is about signal-to-noise, not permission,
    /// so nothing it hides becomes unreachable.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterCountText))]
    private bool _equippableOnly = true;

    partial void OnEquippableOnlyChanged(bool value) => ApplyFilter();

    /// <summary>Status-bar text reflecting filter state.</summary>
    public string FilterCountText
    {
        get
        {
            var total = _allSockets.Count;
            if (total == 0)
            {
                return string.Empty;
            }
            // Any active filter — text or equippable-only — makes the
            // "N of M" form the honest one; saying "3,510 slot(s)" while
            // showing 754 is what made the old readout misleading.
            if (Sockets.Count == total)
            {
                return $"{total} slot(s).";
            }
            return $"{Sockets.Count} of {total} slot(s) shown.";
        }
    }

    /// <summary>
    /// Recompute <see cref="Sockets"/> from <see cref="_allSockets"/>
    /// using <see cref="SearchText"/>. Two-pass:
    /// <list type="number">
    ///   <item>Pass 1 finds every <i>item</i> (identified by
    ///     <c>(BlockIndex, BagIndex, ItemIndex)</c>) whose identity
    ///     fields match (bag / item name / item key). Every slot
    ///     of those items is included — so empty Fill-able slots
    ///     surface alongside the filled ones when the user is
    ///     searching for an item by name.</item>
    ///   <item>Pass 2 also includes any individual slot whose gem
    ///     name / gem key matches, even if its parent item didn't
    ///     match — so searching for a specific gem still works
    ///     across items.</item>
    /// </list>
    /// Called whenever <see cref="SearchText"/> changes. Synchronous
    /// — the snapshot is in-memory and even very generous saves cap
    /// at a few thousand rows.
    /// </summary>
    private void ApplyFilter()
    {
        Sockets.Clear();
        var needle = SearchText;
        var hasNeedle = !string.IsNullOrWhiteSpace(needle);

        // Pass 1: collect item identities whose parent matches the
        // needle, so every slot of a matching item surfaces (including
        // the empty, fillable ones).
        HashSet<(int, int, int, int, int)>? matchedItems = null;
        if (hasNeedle)
        {
            matchedItems = new HashSet<(int, int, int, int, int)>();
            foreach (var row in _allSockets)
            {
                if (row.MatchesItemFilter(needle!))
                {
                    matchedItems.Add(row.ItemIdentity);
                }
            }
        }

        // Pass 2: emit the survivors, collecting the visible items as we
        // go so the Apply-Set dropdown narrows in lockstep with the grid.
        var visibleItems = new HashSet<(int, int, int, int, int)>();
        foreach (var row in _allSockets)
        {
            if (EquippableOnly && !row.IsEquippable)
            {
                continue;
            }
            if (hasNeedle
                && !matchedItems!.Contains(row.ItemIdentity)
                && !row.MatchesSocketFilter(needle!))
            {
                continue;
            }
            Sockets.Add(row);
            visibleItems.Add(row.ItemIdentity);
        }
        PublishApplySetTargets(visibleItems);
        OnPropertyChanged(nameof(FilterCountText));
    }

    /// <summary>
    /// Republish <see cref="ApplySetTargets"/> from
    /// <see cref="_allApplySetTargets"/>, keeping only the items in
    /// <paramref name="visibleItems"/> (pass <c>null</c> for "no filter
    /// — show everything").
    /// </summary>
    /// <remarks>
    /// The dropdown has to track the grid's filter: with 702 items in a
    /// generous save, picking the one you just filtered down to means
    /// scrolling past every other item, which defeats the filter. If the
    /// current <see cref="SelectedTarget"/> falls outside the new list it
    /// is cleared, so Apply can never run against an item the user can't
    /// see.
    /// </remarks>
    private void PublishApplySetTargets(HashSet<(int, int, int, int, int)>? visibleItems)
    {
        ApplySetTargets.Clear();
        foreach (var t in _allApplySetTargets)
        {
            if (visibleItems is null || visibleItems.Contains(t.ItemIdentity))
            {
                ApplySetTargets.Add(t);
            }
        }
        if (SelectedTarget is { } sel
            && !ApplySetTargets.Contains(sel))
        {
            SelectedTarget = null;
        }
    }

    /// <summary>
    /// Distinct items present in the editor — drives the Apply-Set
    /// "target item" dropdown. Each entry collapses every SocketRow
    /// that belongs to the same physical item into a single picker
    /// option so the user picks an item, not a slot.
    /// </summary>
    public ObservableCollection<GemSetTargetItem> ApplySetTargets { get; } = new();

    /// <summary>
    /// Unfiltered snapshot behind <see cref="ApplySetTargets"/>, built
    /// once in <see cref="BuildApplySetState"/>.
    /// <see cref="PublishApplySetTargets"/> republishes the visible
    /// subset from it on every filter change.
    /// </summary>
    private readonly List<GemSetTargetItem> _allApplySetTargets = new();

    /// <summary>Full gem-set catalog (built-in + user-custom).</summary>
    public ObservableCollection<GemSetOption> AvailableGemSets { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyGemSetCommand))]
    private GemSetTargetItem? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyGemSetCommand))]
    private GemSetOption? _selectedSet;

    /// <summary>
    /// Becomes true after the first successful Apply. The hosting
    /// MainWindowViewModel reads this on dialog close to flip its own
    /// dirty flag so the user gets a "*" in the title until Save.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Raised when a row's Change Gem button is clicked. The hosting
    /// MainWindow code-behind handles the gem-picker presentation and
    /// drives the result back into <see cref="ApplyGemPick"/> when the
    /// user makes a selection.
    /// </summary>
    public event Action<SocketRow>? ChangeGemRequested;

    private SocketEditorViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath)
    {
        _loader = loader;
        _localization = localization;
        _journal = journal;
        _savePath = savePath;
    }

    /// <summary>
    /// Build the view-model against a loaded save. Returns null when no
    /// filled sockets exist — caller surfaces an alert rather than
    /// opening an empty window.
    /// </summary>
    public static SocketEditorViewModel? TryCreate(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        IReadOnlyList<CustomGemSet>? customSets = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrEmpty(savePath);

        var vm = new SocketEditorViewModel(loader, localization, journal, savePath);
        vm.CollectViaAllItems();
        if (vm.Sockets.Count == 0)
        {
            return null;
        }
        vm.BuildApplySetState(customSets);
        // CollectViaAllItems fills Sockets unfiltered; apply the default
        // equippable-only view before anything reads the counts.
        vm.ApplyFilter();
        var filledCount = 0;
        foreach (var r in vm.Sockets) if (r.IsFilled) filledCount++;
        vm.StatusMessage = UiText.Format("SocketHeaderStatus",
            "{0} slot(s) across {1} item(s) ({2} filled).",
            vm.Sockets.Count, CountDistinctItems(vm.Sockets), filledCount);
        // Publish the initial filter-count text now that _allSockets
        // is populated. ApplyFilter normally raises this when
        // SearchText changes, but we never went through that path
        // during construction.
        vm.OnPropertyChanged(nameof(FilterCountText));
        return vm;
    }

    /// <summary>
    /// Build the Apply-Set dropdown state from the collected sockets +
    /// the user's custom-set persistence. Distinct items become
    /// target dropdown rows; built-in + custom sets become set
    /// dropdown rows (custom sets with empty <c>GemKeys</c> are
    /// skipped as "undefined").
    /// </summary>
    private void BuildApplySetState(IReadOnlyList<CustomGemSet>? customSets)
    {
        // Distinct items: collapse SocketRows sharing the same
        // SocketRow.ItemIdentity into one entry. Preserve insertion
        // order so the dropdown matches the user's mental scroll order
        // in the main DataGrid.
        var seen = new HashSet<(int, int, int, int, int)>();
        foreach (var r in Sockets)
        {
            if (seen.Add(r.ItemIdentity))
            {
                _allApplySetTargets.Add(new GemSetTargetItem(
                    r.BlockIndex, r.InventoryListFieldIdx, r.BagIndex,
                    r.ItemListFieldIdx, r.ItemIndex,
                    DisplayName: $"{r.BagLabel} · {r.ItemName} ({r.MaxSocketCount} slot{(r.MaxSocketCount == 1 ? "" : "s")})",
                    MaxSocketCount: r.MaxSocketCount));
            }
        }
        PublishApplySetTargets(null);
        // Built-in sets.
        foreach (var bi in BuiltInGemSets.All)
        {
            AvailableGemSets.Add(GemSetOption.From(bi, _localization));
        }
        // Custom sets — skip undefined slots.
        if (customSets is { Count: > 0 })
        {
            foreach (var cs in customSets)
            {
                if (cs.GemKeys is null || cs.GemKeys.Length == 0) continue;
                var label = string.IsNullOrWhiteSpace(cs.Label)
                    ? $"Custom Set ({cs.GemKeys.Length} gem{(cs.GemKeys.Length == 1 ? "" : "s")})"
                    : cs.Label;
                AvailableGemSets.Add(GemSetOption.From(
                    new GemSet(label, cs.GemKeys), _localization));
            }
        }
    }

    /// <summary>
    /// Re-build the custom-set portion of <see cref="AvailableGemSets"/>
    /// after the user edits / saves them via the custom-set editor
    /// dialog. Keeps the 3 built-in entries in place + replaces every
    /// subsequent entry with the freshly-persisted custom set list.
    /// </summary>
    public void RefreshCustomGemSets(IReadOnlyList<CustomGemSet> customSets)
    {
        // Drop everything past the built-in section.
        while (AvailableGemSets.Count > BuiltInGemSets.All.Count)
        {
            AvailableGemSets.RemoveAt(AvailableGemSets.Count - 1);
        }
        // Re-add custom sets (skipping undefined slots).
        if (customSets is not null)
        {
            foreach (var cs in customSets)
            {
                if (cs.GemKeys is null || cs.GemKeys.Length == 0) continue;
                var label = string.IsNullOrWhiteSpace(cs.Label)
                    ? $"Custom Set ({cs.GemKeys.Length} gem{(cs.GemKeys.Length == 1 ? "" : "s")})"
                    : cs.Label;
                AvailableGemSets.Add(GemSetOption.From(
                    new GemSet(label, cs.GemKeys), _localization));
            }
        }
        StatusMessage = UiText.Format("SocketGemSetsRefreshed",
            "Custom gem sets refreshed — {0} set(s) available in the Apply-Set dropdown.", AvailableGemSets.Count);
    }

    /// <summary>
    /// Apply the selected set to the selected target item.
    /// Per-slot routing: empty → Fill, filled-different → Change,
    /// filled-same → no-op. Slots past <c>set.GemKeys.Count</c> are
    /// left alone (per user contract: "1-entry set overwrites slot
    /// 0 only").
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyGemSet))]
    private void ApplyGemSet()
    {
        if (SelectedTarget is not { } target || SelectedSet is not { } set)
        {
            return;
        }
        // Find every SocketRow for this target — iterate in
        // SocketIndex order so the per-slot apply maps cleanly.
        // Walk _allSockets, not the filtered Sockets view. A gem-set
        // apply addresses the whole item, and a filter that hides some
        // of its slots (e.g. searching a gem name, which matches only
        // the filled ones) must not silently reduce the set to the
        // visible slots.
        var rows = new SortedDictionary<int, SocketRow>();
        foreach (var r in _allSockets)
        {
            if (r.ItemIdentity == target.ItemIdentity)
            {
                rows[r.SocketIndex] = r;
            }
        }
        // Gate the whole batch once, up front: inside RunDeferred the
        // version doesn't move, so the per-slot check can't see a
        // concurrent edit that landed before we began.
        if (rows.Count > 0 && RejectIfSnapshotStale(rows.Values.First()))
        {
            return;
        }
        var applyCount = Math.Min(set.GemKeys.Count, target.MaxSocketCount);
        var changed = 0;
        // Wrap the per-slot Apply loop in a deferred-redecode batch
        // (see vendor/crimson-rs/docs/save-deferred-redecode.md). Each
        // empty→fill transition fires SetScalarFieldsPresentBatch which
        // is length-changing; without the batch every flip triggers a
        // full body re-decode (~25ms on a 5MB body), so a 5-slot Apply
        // pays ~5 re-decodes. With the batch every flip stays in the
        // in-memory tree and the trailing commit runs ONE encode +
        // parse + decode pass.
        //
        // ApplyGemPick catches CrimsonSaveException internally + sets
        // row.LastError + returns void, so the loop never lets an
        // exception escape — the deferred batch sees normal completion
        // and commits the partial progress (matches the pre-batch
        // partial-success UX). A commit-time MUTATION_INVALID surfaces
        // as the outer try/catch falling through to the error footer.
        var failed = 0;
        try
        {
            _loader.RunDeferred(() =>
            {
                _deferredDepth++;
                try
                {
                    for (var i = 0; i < applyCount; i++)
                    {
                        if (!rows.TryGetValue(i, out var row)) continue;
                        var newKey = set.GemKeys[i];
                        if (row.IsFilled && row.CurrentGemKey == newKey)
                        {
                            continue; // already what we want
                        }
                        // Count only what actually landed. ApplyGemPick
                        // swallows its own failures (socket-open abort,
                        // CrimsonSaveException) and returns false;
                        // incrementing regardless used to report "N
                        // slot(s) changed" for a run that wrote nothing,
                        // and overwrite the per-slot error text with it.
                        if (ApplyGemPick(row, newKey))
                        {
                            changed++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
                finally
                {
                    _deferredDepth--;
                }
            });
        }
        catch (CrimsonSaveException commitEx)
        {
            StatusMessage = UiText.Format("SocketApplySetFailed",
                "Apply Set: {0} — commit failed after {1} slot(s): {2} (code {3}). "
                + "Reload the save without writing to revert.",
                set.Label, changed, commitEx.Message, commitEx.ErrorCode);
            return;
        }
        // The commit re-decoded, so every promoted field has a fresh
        // byte range again.
        foreach (var r in _allSockets)
        {
            r.ValidSocketCountRangeStale = false;
        }
        ResyncSnapshotVersion();
        if (failed > 0)
        {
            // Leave the per-slot failure text from ApplyGemPick standing
            // — it names the slot and the native error — and only add the
            // tally in the journal.
            _journal.Log(UiText.Get("JournalCatSockets", "Sockets"),
                UiText.Format("JournalSocketApplySetPartial",
                    "Applied set \"{0}\" to {1} — {2} slot(s) changed, {3} failed",
                    set.Label, target.DisplayName, changed, failed));
            return;
        }
        if (changed == 0)
        {
            StatusMessage = UiText.Format("SocketApplySetNoChange",
                "Apply Set: {0} — no changes (every targeted slot already matches).", set.Label);
            return;
        }
        _journal.Log(UiText.Get("JournalCatSockets", "Sockets"),
            UiText.Format("JournalSocketApplySet", "Applied set \"{0}\" to {1} — {2} slot(s) changed",
                set.Label, target.DisplayName, changed));
        StatusMessage = UiText.Format("SocketApplySetDone",
            "Applied set \"{0}\" to {1}: {2} slot(s) changed.", set.Label, target.DisplayName, changed);
    }

    private bool CanApplyGemSet =>
        SelectedTarget is not null && SelectedSet is not null;

    private static int CountDistinctItems(IEnumerable<SocketRow> rows)
    {
        var seen = new HashSet<(int, int, int, int, int)>();
        foreach (var r in rows)
        {
            seen.Add(r.ItemIdentity);
        }
        return seen.Count;
    }

    /// <summary>
    /// Collect socket rows via the single-FFI
    /// <see cref="ISaveLoader.ListAllItems"/> enumerator — covers all
    /// five container kinds (active equip / reserve / inventory /
    /// mercenary equip / mercenary inventory) in one walk. Filters by
    /// <see cref="ItemRecordFlags.HasSocketData"/> (skip items with no
    /// socket list) and
    /// <see cref="LocalizationProvider.IsPlayerEditableItem"/> (drop
    /// NPC followers' gear; widen for player-controlled mounts whose
    /// <c>_ownedCharacterKey</c> is absent).
    /// </summary>
    private void CollectViaAllItems()
    {
        var detailsCache = new Dictionary<uint, BlockDetails>();
        // Stamp the snapshot BEFORE the walk, not inside it: an empty
        // item list would otherwise leave _snapshotVersion at 0 and make
        // IsSnapshotStale permanently true.
        var records = _loader.ListAllItems(out var version);
        _snapshotVersion = version;
        foreach (var rec in records)
        {
            if (!rec.HasSocketData) continue;
            if (!_localization.IsPlayerEditableItem(rec)) continue;
            if (!detailsCache.TryGetValue(rec.BlockIndex, out var top))
            {
                try
                {
                    top = _loader.LoadBlockDetails(_savePath, (int)rec.BlockIndex);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                detailsCache[rec.BlockIndex] = top;
            }
            var item = DescendToItem(top, rec);
            if (item is null) continue;
            CollectFromItem(
                blockIndex: (int)rec.BlockIndex,
                firstStepFieldIdx: (int)rec.PathStep0Field,
                firstStepElementIdx: (int)rec.PathStep0Element,
                secondStepFieldIdx: (int)rec.PathStep1Field,
                secondStepElementIdx: (int)rec.PathStep1Element,
                item: item,
                bagLabel: _localization.FormatItemSourceLabel(rec));
        }
    }

    /// <summary>
    /// Descend an <see cref="ItemRecord"/>'s 2-step path from the
    /// top-level block down to the inner <c>ItemSaveData</c>. Step 0
    /// is always <c>ObjectList</c>; step 1 is <c>ObjectList</c> for
    /// inventory / mercenary kinds and <c>ObjectLocator</c> for
    /// active equip / reserve. Returns null on snapshot staleness
    /// (defensive — shouldn't happen on a fresh
    /// <see cref="ISaveLoader.ListAllItems"/> read).
    /// </summary>
    private static BlockDetails? DescendToItem(BlockDetails top, ItemRecord rec)
    {
        if (rec.PathLen != 2) return null;
        var step0Field = top.Fields.FirstOrDefault(
            f => f.FieldIndex == rec.PathStep0Field);
        if (step0Field?.Elements is not { } step0Elements
            || rec.PathStep0Element >= step0Elements.Count)
        {
            return null;
        }
        var step1Host = step0Elements[(int)rec.PathStep0Element];
        var step1Field = step1Host.Fields.FirstOrDefault(
            f => f.FieldIndex == rec.PathStep1Field);
        if (step1Field is null) return null;
        if (step1Field.Child is { } locatorChild
            && step1Field.Elements is not { Count: > 0 })
        {
            return locatorChild;
        }
        if (step1Field.Elements is { } step1Elements
            && rec.PathStep1Element < step1Elements.Count)
        {
            return step1Elements[(int)rec.PathStep1Element];
        }
        return null;
    }

    private void CollectFromItem(
        int blockIndex,
        int firstStepFieldIdx,
        int firstStepElementIdx,
        int secondStepFieldIdx,
        int secondStepElementIdx,
        BlockDetails item,
        string bagLabel)
    {
        uint itemKey = 0;
        DecodedFieldRow? socketListField = null;
        int validSocketCountFieldIdx = -1;
        byte currentValidSocketCount = 0;
        var validSocketCountPresent = false;
        foreach (var f in item.Fields)
        {
            if (string.Equals(f.Name, ItemKeyFieldName, StringComparison.Ordinal)
                && f.Present
                && TryParseScalarUInt(f.Value, out var ik)
                && ik <= uint.MaxValue)
            {
                itemKey = (uint)ik;
            }
            else if (string.Equals(f.Name, SocketListFieldName, StringComparison.Ordinal))
            {
                socketListField = f;
            }
            else if (string.Equals(f.Name, ValidSocketCountFieldName, StringComparison.Ordinal))
            {
                validSocketCountFieldIdx = f.FieldIndex;
                // The game encodes "no socket has ever been opened" as
                // the field being ABSENT, never as an explicit 0 — the
                // value 0 does not occur once across the 5,556
                // socket-capable items in the reference saves (5,278 of
                // them have it absent). Presence therefore decides
                // whether opening a socket is an in-place scalar write
                // or an absent → present promotion.
                validSocketCountPresent = f.Present;
                if (f.Present
                    && TryParseScalarUInt(f.Value, out var vsc)
                    && vsc <= byte.MaxValue)
                {
                    currentValidSocketCount = (byte)vsc;
                }
            }
        }
        if (socketListField?.Elements is not { Count: > 0 } sockets)
        {
            return;
        }
        // One iteminfo lookup per item, not per slot. Unknown key or an
        // unloaded bridge => treat as equippable so nothing disappears.
        var isEquippable = _localization.LookupItemInfoSummary(itemKey) is not { } gd
                           || gd.EquipTypeInfo != 0;
        var (itemNameEn, itemNameSecondary) = ResolveItemNames(_localization, itemKey);
        var itemName = FormatCombinedName(itemNameEn, itemNameSecondary);
        // Capture the per-element field indices once from the first
        // socket — the per-class schema is fixed across siblings.
        var (gemKeyFieldIdx, enduranceFieldIdx) = ResolveSocketFieldIndices(sockets[0]);
        if (gemKeyFieldIdx < 0 || enduranceFieldIdx < 0)
        {
            // Schema drift — the socket element's expected fields are
            // missing; skip the whole item rather than building
            // misaddressed rows.
            return;
        }
        // Add ALL slots (empty + filled). Per user request, no cap by
        // gamedata or by the save's _validSocketCount — every entry in
        // _socketSaveDataList[] is editable.
        for (var s = 0; s < sockets.Count; s++)
        {
            var (isFilled, gemKey) = ReadSocketState(sockets[s], gemKeyFieldIdx);
            var gemName = isFilled
                ? FormatItemDisplay(_localization, gemKey)
                : string.Empty;
            var row = new SocketRow(
                vm: this,
                blockIndex: blockIndex,
                inventoryListFieldIdx: firstStepFieldIdx,
                bagIndex: firstStepElementIdx,
                itemListFieldIdx: secondStepFieldIdx,
                itemIndex: secondStepElementIdx,
                socketListFieldIdx: socketListField.FieldIndex,
                socketIndex: s,
                gemKeyFieldIdx: gemKeyFieldIdx,
                enduranceFieldIdx: enduranceFieldIdx,
                validSocketCountFieldIdx: validSocketCountFieldIdx,
                maxSocketCount: sockets.Count,
                currentValidSocketCount: currentValidSocketCount,
                validSocketCountPresent: validSocketCountPresent,
                isEquippable: isEquippable,
                bagLabel: bagLabel,
                itemKey: itemKey,
                itemName: itemName,
                itemNameEnglish: itemNameEn,
                itemNameSecondary: itemNameSecondary,
                isFilled: isFilled,
                currentGemKey: isFilled ? gemKey : 0u,
                currentGemName: gemName);
            _allSockets.Add(row);
            Sockets.Add(row);
        }
    }

    /// <summary>
    /// Per-socket field-index lookup. Returns <c>(-1, -1)</c> when the
    /// socket element doesn't carry the expected schema — caller
    /// skips the whole item in that case.
    /// </summary>
    private static (int GemKeyFieldIdx, int EnduranceFieldIdx)
        ResolveSocketFieldIndices(BlockDetails socket)
    {
        int gemKeyIdx = -1;
        int enduranceIdx = -1;
        foreach (var sf in socket.Fields)
        {
            if (string.Equals(sf.Name, ItemKeyFieldName, StringComparison.Ordinal))
            {
                gemKeyIdx = sf.FieldIndex;
            }
            else if (string.Equals(sf.Name, EnduranceFieldName, StringComparison.Ordinal))
            {
                enduranceIdx = sf.FieldIndex;
            }
        }
        return (gemKeyIdx, enduranceIdx);
    }

    /// <summary>
    /// Read whether a socket slot is filled and (if so) its gem key.
    /// Empty slots have <c>_itemKey</c> absent OR equal to 0.
    /// </summary>
    private static (bool IsFilled, uint GemKey)
        ReadSocketState(BlockDetails socket, int gemKeyFieldIdx)
    {
        if (gemKeyFieldIdx < 0 || gemKeyFieldIdx >= socket.Fields.Count)
        {
            return (false, 0);
        }
        var gemField = socket.Fields[gemKeyFieldIdx];
        if (!gemField.Present
            || !TryParseScalarUInt(gemField.Value, out var keyU64)
            || keyU64 == 0
            || keyU64 > uint.MaxValue)
        {
            return (false, 0);
        }
        return (true, (uint)keyU64);
    }

    /// <summary>
    /// Called by <see cref="SocketRow.ChangeGemCommand"/> — forwards to
    /// the dialog code-behind to open a gem-filtered Item Picker.
    /// </summary>
    internal void RequestChangeGem(SocketRow row) => ChangeGemRequested?.Invoke(row);

    /// <summary>
    /// Apply a user-picked gem to <paramref name="row"/>. Routes by
    /// state: empty → batch-fill (promote endurance + itemkey to
    /// present), filled → in-place change (overwrite itemkey + reset
    /// endurance). Same-gem-as-current is a no-op. Auto-bumps
    /// <c>_validSocketCount</c> when the slot index is past the
    /// current count so the slot becomes visible in-game.
    /// </summary>
    public bool ApplyGemPick(SocketRow row, uint newGemKey)
    {
        if (RejectIfSnapshotStale(row))
        {
            return false;
        }
        // Open the slot FIRST. A gem sitting at an index the item's
        // _validSocketCount doesn't cover is a state the game never
        // writes (all 734 filled sockets in the reference saves sit
        // inside their item's opened window) and the engine
        // rejects it — the item comes back with every socket sealed.
        // Doing the count before the gem means a failure here leaves
        // the save consistent instead of stranding an orphan gem.
        //
        // This runs BEFORE the same-gem short-circuit on purpose: a slot
        // that is filled but sits outside the window is exactly what
        // StateLabel flags as "bump on next edit", and re-picking the
        // gem already there is the obvious way a user tries to trigger
        // that repair.
        if (!TryEnsureSocketOpened(row))
        {
            return false;
        }
        if (row.IsFilled && newGemKey == row.CurrentGemKey)
        {
            StatusMessage = UiText.Get("SocketSameGem", "Same gem as current — no write performed.");
            return false;
        }
        // A gem must carry its own gamedata max_endurance. Without
        // iteminfo we cannot know it, and guessing reinstates exactly the
        // above-the-cap value that makes the engine reject the item — so
        // refuse rather than write something plausible-looking.
        if (ResolveGemEndurance(newGemKey) is not { } endurance)
        {
            row.LastError = "iteminfo not loaded — gem durability unknown";
            StatusMessage = UiText.Get("SocketNoIteminfo",
                "Game data (iteminfo) isn't loaded, so a gem's durability can't be read. "
                + "Point the editor at the game install and reopen this dialog — writing a "
                + "guessed durability makes the game reject the item's sockets.");
            return false;
        }
        var pathToSocket = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
            new PathStep((uint)row.SocketListFieldIdx, (uint)row.SocketIndex),
        };
        var enduranceBytes = BitConverter.GetBytes(endurance);
        var keyBytes = BitConverter.GetBytes(newGemKey);

        try
        {
            if (!row.IsFilled)
            {
                // Empty → fill. Both fields go absent → present in one
                // batch so the slot transitions atomically; mask flips
                // from [0x00] to [0x03] in one re-emit.
                var ops = new List<ScalarPresentBatchOp>
                {
                    new ScalarPresentBatchOp(
                        row.BlockIndex, pathToSocket, row.EnduranceFieldIdx,
                        MakePresent: true, enduranceBytes),
                    new ScalarPresentBatchOp(
                        row.BlockIndex, pathToSocket, row.GemKeyFieldIdx,
                        MakePresent: true, keyBytes),
                };
                _loader.SetScalarFieldsPresentBatch(ops);
            }
            else
            {
                // Filled → change. Overwrite both fields in place;
                // resetting _currentEndurance to max is the durability
                // fix v1 missed (greater gems used to inherit the old
                // slot's worn value when swapped).
                _loader.SetScalarField(row.BlockIndex, pathToSocket,
                    row.GemKeyFieldIdx, keyBytes);
                _loader.SetScalarField(row.BlockIndex, pathToSocket,
                    row.EnduranceFieldIdx, enduranceBytes);
            }
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = UiText.Format("SocketApplyFailed",
                "Apply failed ({0}, item {1}, socket {2}): {3}",
                row.BagLabel, row.ItemName, row.SocketIndex, ex.Message);
            row.LastError = ex.Message;
            return false;
        }
        // Mirror state back so the row UI repaints and a follow-up
        // edit of the same slot routes through the "filled" branch
        // without a reload.
        var newGemName = FormatItemDisplay(_localization, newGemKey);
        var wasFilled = row.IsFilled;
        row.AppliedGemKey = newGemKey;
        row.AppliedGemName = newGemName;
        row.SetFilled(newGemKey, newGemName);
        row.LastError = null;
        IsDirty = true;
        _journal.Log(UiText.Get("JournalCatSockets", "Sockets"),
            wasFilled
                ? UiText.Format("JournalSocketSet", "Set gem in {0} socket {1} → {2}",
                    row.ItemName, row.SocketIndex, newGemName)
                : UiText.Format("JournalSocketFilled", "Filled gem in {0} socket {1} → {2}",
                    row.ItemName, row.SocketIndex, newGemName));
        StatusMessage = wasFilled
            ? UiText.Format("SocketSetDone", "Set gem in {0} socket {1}: → {2}.",
                row.ItemName, row.SocketIndex, newGemName)
            : UiText.Format("SocketFilledDone", "Filled gem in {0} socket {1}: → {2}.",
                row.ItemName, row.SocketIndex, newGemName);
        ResyncSnapshotVersion();
        return true;
    }

    /// <summary>
    /// Clear a filled socket: both <c>_currentEndurance</c> +
    /// <c>_itemKey</c> are demoted to absent in one batch so the mask
    /// flips from <c>[0x03]</c> back to <c>[0x00]</c>. No-op on
    /// already-empty rows.
    /// </summary>
    internal void ApplyClear(SocketRow row)
    {
        if (!row.IsFilled || RejectIfSnapshotStale(row))
        {
            return;
        }
        var pathToSocket = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
            new PathStep((uint)row.SocketListFieldIdx, (uint)row.SocketIndex),
        };
        try
        {
            var ops = new List<ScalarPresentBatchOp>
            {
                new ScalarPresentBatchOp(
                    row.BlockIndex, pathToSocket, row.EnduranceFieldIdx,
                    MakePresent: false, Array.Empty<byte>()),
                new ScalarPresentBatchOp(
                    row.BlockIndex, pathToSocket, row.GemKeyFieldIdx,
                    MakePresent: false, Array.Empty<byte>()),
            };
            _loader.SetScalarFieldsPresentBatch(ops);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = UiText.Format("SocketClearFailed",
                "Clear failed ({0}, item {1}, socket {2}): {3}",
                row.BagLabel, row.ItemName, row.SocketIndex, ex.Message);
            row.LastError = ex.Message;
            return;
        }
        var prevName = row.AppliedGemName ?? row.CurrentGemName;
        row.SetEmpty();
        row.LastError = null;
        IsDirty = true;
        _journal.Log(UiText.Get("JournalCatSockets", "Sockets"),
            UiText.Format("JournalSocketCleared", "Cleared gem in {0} socket {1} (was {2})",
                row.ItemName, row.SocketIndex, prevName));
        StatusMessage = UiText.Format("SocketClearDone", "Cleared gem in {0} socket {1} (was {2}).",
            row.ItemName, row.SocketIndex, prevName);
        ResyncSnapshotVersion();
    }

    /// <summary>
    /// Make sure the parent item's <c>_validSocketCount</c> covers
    /// <paramref name="row"/>'s slot index, so the slot is actually
    /// open in-game. Returns <c>true</c> when the slot is (now) open
    /// and the caller may write the gem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two distinct starting states, and getting them confused is what
    /// broke socket editing outright:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Field present</b> (the item has had at least one
    ///     socket opened): an in-place
    ///     <see cref="ISaveLoader.SetScalarField"/> raising the u8 is
    ///     enough.</item>
    ///   <item><b>Field ABSENT</b> (a never-socketed item — the game's
    ///     encoding for "zero opened sockets"; it never writes an
    ///     explicit 0): the field has no byte range, so the in-place
    ///     setter fails with <c>NOT_SCALAR (-12)</c> and the count
    ///     stays absent. It has to be promoted absent → present via
    ///     <see cref="ISaveLoader.SetScalarFieldPresent"/>.</item>
    /// </list>
    /// <para>
    /// The old code only ever did the in-place write and swallowed the
    /// failure into <c>row.LastError</c>, so every fill on a
    /// never-socketed item produced a gem in a socket the engine
    /// considers unopened. The game treats that as bad data and
    /// re-seals every socket on the item.
    /// </para>
    /// </remarks>
    /// <summary>
    /// True when the save has been mutated by something other than this
    /// dialog since its rows were built, so every row's
    /// <c>(bag, item)</c> element index may now address a different item.
    /// </summary>
    /// <remarks>
    /// The Tools dialogs are non-modal over a live main window sharing
    /// one loader handle, and a length-changing edit there (Remove
    /// Element, or an item clone) shifts every later element index in
    /// the same list. Writing through a stale row would then open a
    /// socket and drop a gem into an item the user never selected — the
    /// exact filled-but-sealed shape this editor exists to avoid, on an
    /// innocent item and with no error raised. So writes are gated
    /// rather than attempted. Required by
    /// vendor/crimson-rs/docs/save-mutation-version.md, which names the
    /// socket editor specifically.
    /// </remarks>
    private bool IsSnapshotStale
    {
        get
        {
            if (_deferredDepth > 0)
            {
                // A deferred batch bumps the version once, at commit, so
                // mid-batch there is nothing new to compare against.
                return false;
            }
            try
            {
                return _loader.GetMutationVersion() != _snapshotVersion;
            }
            catch (InvalidOperationException)
            {
                return false; // save unloaded under us — nothing to guard
            }
        }
    }

    /// <summary>
    /// Re-baseline <see cref="_snapshotVersion"/> after this dialog's own
    /// write, so our mutations don't read as somebody else's.
    /// </summary>
    private void ResyncSnapshotVersion()
    {
        if (_deferredDepth > 0)
        {
            return; // the commit bumps once; resync happens after it
        }
        try
        {
            _snapshotVersion = _loader.GetMutationVersion();
        }
        catch (InvalidOperationException)
        {
            // Save unloaded — leave the baseline alone.
        }
    }

    /// <summary>
    /// Refuse a write against a stale snapshot and tell the user why.
    /// </summary>
    private bool RejectIfSnapshotStale(SocketRow row)
    {
        if (!IsSnapshotStale)
        {
            return false;
        }
        row.LastError = "save changed elsewhere — reopen this dialog";
        StatusMessage = UiText.Get("SocketSnapshotStale",
            "The save was modified in another window since this list was built, so the "
            + "rows may no longer point at the items they name. Close and reopen "
            + "Tools → Edit Item Sockets before editing.");
        return true;
    }

    private bool TryEnsureSocketOpened(SocketRow row)
    {
        if (row.ValidSocketCountFieldIdx < 0)
        {
            // Schema doesn't carry the field at all — nothing to
            // maintain, and nothing we can check. Let the write through
            // (matches the pre-existing permissive contract).
            return true;
        }
        // Clamp UP only. The write is absolute, so without the Max a
        // stale mirror (two dialogs open over one loader) could LOWER the
        // count and strand already-installed gems outside the window.
        var needed = (byte)Math.Max(
            row.CurrentValidSocketCount, Math.Min(byte.MaxValue, row.SocketIndex + 1));
        if (row.ValidSocketCountPresent && row.CurrentValidSocketCount >= needed)
        {
            return true;
        }
        var pathToItem = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
        };
        try
        {
            if (!row.ValidSocketCountPresent)
            {
                _loader.SetScalarFieldPresent(row.BlockIndex, pathToItem,
                    row.ValidSocketCountFieldIdx, makePresent: true, new[] { needed });
                // Inside a deferred batch the promotion leaves the field's
                // decoded byte range at start == end == 0 until the commit
                // re-decodes, so a later in-place raise would compute
                // expected = 0 and fail LENGTH_MISMATCH. Remember that.
                row.ValidSocketCountRangeStale = _deferredDepth > 0;
            }
            else if (row.ValidSocketCountRangeStale)
            {
                // Stale range: the in-place setter is unusable. The
                // presence surface writes the value from init_bytes and
                // never reads start/end, but present(1) on an already-
                // present field is a documented no-op — so clear it
                // first. Both halves stay inside the same deferred batch,
                // so the save is never observably missing the field.
                _loader.SetScalarFieldPresent(row.BlockIndex, pathToItem,
                    row.ValidSocketCountFieldIdx, makePresent: false, ReadOnlySpan<byte>.Empty);
                _loader.SetScalarFieldPresent(row.BlockIndex, pathToItem,
                    row.ValidSocketCountFieldIdx, makePresent: true, new[] { needed });
            }
            else
            {
                _loader.SetScalarField(row.BlockIndex, pathToItem,
                    row.ValidSocketCountFieldIdx, new[] { needed });
            }
        }
        catch (CrimsonSaveException ex)
        {
            // Hard-fail the whole edit: writing the gem anyway would
            // leave a filled socket outside the opened window, which is
            // exactly the shape the engine rejects.
            row.LastError = ex.Message;
            StatusMessage = UiText.Format("SocketOpenFailed",
                "Could not open socket {0} on {1}: {2} (code {3}). Gem not written.",
                row.SocketIndex, $"{row.BagLabel} · {row.ItemName}", ex.Message, ex.ErrorCode);
            return false;
        }
        // Propagate to every row of the same item so subsequent edits
        // see the new count and don't re-write it. Walk _allSockets,
        // not the filtered Sockets view: with a filter active the
        // hidden rows of this same item would otherwise keep a stale
        // "not present" flag and try to promote an already-present
        // field on the next edit.
        foreach (var r in _allSockets)
        {
            if (r.ItemIdentity == row.ItemIdentity)
            {
                r.CurrentValidSocketCount = needed;
                r.ValidSocketCountPresent = true;
                r.ValidSocketCountRangeStale = row.ValidSocketCountRangeStale;
            }
        }
        return true;
    }

    /// <summary>
    /// The <c>_currentEndurance</c> a freshly-socketed
    /// <paramref name="gemKey"/> must carry: the gem's own
    /// <c>iteminfo.max_endurance</c>. <c>null</c> means the iteminfo
    /// bridge isn't loaded, so the value is <b>unknowable</b> and the
    /// caller must refuse the write.
    /// </summary>
    /// <remarks>
    /// Not a heuristic — every gem the game itself socketed across the
    /// reference saves carries exactly this value (100 for the
    /// "AbyssGear_*_Special" durability family, 65535 for the rest),
    /// and a worn gem only ever sits <i>below</i> it (99 / 95 observed).
    /// <para>
    /// The null case matters: <c>LookupItemInfoSummary</c> returns null
    /// both for an unknown key <i>and</i> for a catalog that never
    /// loaded (<c>LocalizationProvider.TryBootstrapItemInfo</c> swallows
    /// a parse failure), and the dialog opens either way. Collapsing the
    /// two into <see cref="DefaultGemEndurance"/> would silently
    /// reinstate the blanket 65535 this fix removed — on every gem at
    /// once. So the catalog is checked separately, and only a genuinely
    /// unknown key inside a loaded catalog (a CE-invented gem, where
    /// there is no cap to respect) takes the fallback.
    /// </para>
    /// </remarks>
    private ushort? ResolveGemEndurance(uint gemKey)
    {
        if (_localization.ItemCount == 0)
        {
            return null; // iteminfo bridge absent — value unknowable
        }
        return _localization.LookupItemInfoSummary(gemKey)?.MaxEndurance
               ?? DefaultGemEndurance;
    }

    /// <summary>
    /// Format <paramref name="itemKey"/> for display in the dialog.
    /// Prefers PALOC-resolved English name → iteminfo string_key →
    /// raw decimal key. Mirrors the resolved-name fallback the main
    /// editor's element view uses.
    /// </summary>
    private static string FormatItemDisplay(LocalizationProvider localization, uint itemKey)
    {
        var formatted = localization.ResolveItemNameFormatted(itemKey);
        if (!string.IsNullOrEmpty(formatted))
        {
            return formatted;
        }
        return localization.ItemInfoStringKey(itemKey) ?? itemKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolve an item key into its English + secondary-language name
    /// pair. English falls back to the iteminfo string_key (then raw
    /// decimal) so the cell never goes blank; secondary stays
    /// <c>null</c> when no secondary language is configured or the
    /// PALOC misses. Both fields feed the filter — substring matches
    /// against either count as a hit.
    /// </summary>
    private static (string English, string? Secondary)
        ResolveItemNames(LocalizationProvider localization, uint itemKey)
    {
        var en = localization.LookupItemName(itemKey, LocalizationProvider.DefaultLanguage)
                 ?? localization.ItemInfoStringKey(itemKey)
                 ?? itemKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var secondaryLang = localization.SecondaryLanguage;
        string? secondary = null;
        if (!string.IsNullOrEmpty(secondaryLang))
        {
            secondary = localization.LookupItemName(itemKey, secondaryLang);
        }
        return (en, secondary);
    }

    /// <summary>
    /// Build the single combined display string the Item column shows.
    /// Mirrors <see cref="LocalizationProvider.ResolveItemNameFormatted"/>'s
    /// shape (<c>"English / 中文"</c>) but driven off the pre-resolved
    /// pair so the filter and the display share one source of truth.
    /// </summary>
    private static string FormatCombinedName(string english, string? secondary) =>
        string.IsNullOrEmpty(secondary) ? english : $"{english} / {secondary}";

    /// <summary>
    /// Pre-formatted scalar value (<c>"123 &lt;u32&gt;"</c>) →
    /// <see cref="ulong"/>. Returns false on signed / float / bytes
    /// values so the caller can skip the field instead of writing a
    /// wrong number.
    /// </summary>
    private static bool TryParseScalarUInt(string formatted, out ulong value)
    {
        value = 0;
        if (!ScalarFieldEditing.TryParse(formatted, out var rawText, out var tag))
        {
            return false;
        }
        if (tag is not ("u8" or "u16" or "u32" or "u64"))
        {
            return false;
        }
        return ulong.TryParse(rawText, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out value);
    }
}

/// <summary>
/// One socket slot row in the Sockets editor v2 dialog. Carries both
/// empty and filled states; per-row commands route by state:
/// <see cref="FillGemCommand"/> opens the picker for empty slots,
/// <see cref="ChangeGemCommand"/> opens it for filled slots, and
/// <see cref="ClearGemCommand"/> demotes a filled slot back to empty.
/// </summary>
public sealed partial class SocketRow : ObservableObject
{
    private readonly SocketEditorViewModel _parent;

    public SocketRow(
        SocketEditorViewModel vm,
        int blockIndex,
        int inventoryListFieldIdx,
        int bagIndex,
        int itemListFieldIdx,
        int itemIndex,
        int socketListFieldIdx,
        int socketIndex,
        int gemKeyFieldIdx,
        int enduranceFieldIdx,
        int validSocketCountFieldIdx,
        int maxSocketCount,
        byte currentValidSocketCount,
        bool validSocketCountPresent,
        bool isEquippable,
        string bagLabel,
        uint itemKey,
        string itemName,
        string itemNameEnglish,
        string? itemNameSecondary,
        bool isFilled,
        uint currentGemKey,
        string currentGemName)
    {
        _parent = vm;
        BlockIndex = blockIndex;
        InventoryListFieldIdx = inventoryListFieldIdx;
        BagIndex = bagIndex;
        ItemListFieldIdx = itemListFieldIdx;
        ItemIndex = itemIndex;
        SocketListFieldIdx = socketListFieldIdx;
        SocketIndex = socketIndex;
        GemKeyFieldIdx = gemKeyFieldIdx;
        EnduranceFieldIdx = enduranceFieldIdx;
        ValidSocketCountFieldIdx = validSocketCountFieldIdx;
        MaxSocketCount = maxSocketCount;
        _currentValidSocketCount = currentValidSocketCount;
        _validSocketCountPresent = validSocketCountPresent;
        IsEquippable = isEquippable;
        BagLabel = bagLabel;
        ItemKey = itemKey;
        ItemName = itemName;
        ItemNameEnglish = itemNameEnglish;
        ItemNameSecondary = itemNameSecondary;
        ItemKeyText = itemKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _isFilled = isFilled;
        _currentGemKey = currentGemKey;
        _currentGemName = currentGemName;
    }

    /// <summary>
    /// English item name (PALOC default-language lookup; falls back to
    /// the iteminfo string_key, then to the raw decimal key). Always
    /// non-empty. Substring-matched by the filter.
    /// </summary>
    public string ItemNameEnglish { get; }

    /// <summary>
    /// Item name in the user's secondary language (e.g. <c>"黃金 / Gold"</c>'s
    /// <c>"黃金"</c> half), <c>null</c> when no secondary language is
    /// configured or the PALOC misses. Substring-matched by the filter
    /// in addition to <see cref="ItemNameEnglish"/>, so users can type
    /// either name and find a hit.
    /// </summary>
    public string? ItemNameSecondary { get; }

    /// <summary>
    /// Pre-formatted <see cref="ItemKey"/> as a decimal string —
    /// stored so the filter can do a substring match against
    /// "12345" without re-stringifying per filter pass.
    /// </summary>
    public string ItemKeyText { get; }

    /// <summary>
    /// True iff <paramref name="needle"/> matches one of the row's
    /// <b>parent item identity</b> fields: bag label, English item
    /// name, secondary item name, item key. Drives the first pass
    /// of the filter — every row of an item whose identity matches
    /// is included (so empty Fill-able slots stay visible when the
    /// user is searching for a specific item, not a specific gem).
    /// Case-insensitive ordinal.
    /// </summary>
    public bool MatchesItemFilter(string needle)
    {
        if (BagLabel.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (ItemNameEnglish.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (ItemNameSecondary is not null
            && ItemNameSecondary.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (ItemKeyText.Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// True iff <paramref name="needle"/> matches the row's
    /// <b>per-slot</b> fields: current gem name, current gem key.
    /// Drives the second pass — slots whose gem matches the filter
    /// (but whose parent item doesn't) are still surfaced. Empty
    /// slots can never match here (no gem to compare against), so
    /// to see them the user has to match the parent item via
    /// <see cref="MatchesItemFilter"/>. Case-insensitive ordinal.
    /// </summary>
    public bool MatchesSocketFilter(string needle)
    {
        if (!string.IsNullOrEmpty(CurrentGemName)
            && CurrentGemName.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (CurrentGemKey != 0
            && CurrentGemKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    public int BlockIndex { get; }

    /// <summary>
    /// First descent step's <b>field index</b> on the top-level block.
    /// Reinterpreted by source:
    /// <list type="bullet">
    ///   <item>Inventory: index of the <c>_inventorylist</c> field
    ///     on <c>InventorySaveData</c>.</item>
    ///   <item>Equipped: index of the <c>_list</c> field on
    ///     <c>EquipmentSaveData</c>.</item>
    /// </list>
    /// The path-addressed ABI treats both as ObjectList descents.
    /// </summary>
    public int InventoryListFieldIdx { get; }

    /// <summary>
    /// First descent step's <b>element index</b>.
    /// Inventory: bag index inside <c>_inventorylist</c>.
    /// Equipped: slot index inside
    /// <c>EquipmentSaveData._list</c> (0..17 in 1.07).
    /// </summary>
    public int BagIndex { get; }

    /// <summary>
    /// Second descent step's <b>field index</b>.
    /// Inventory: index of the <c>_itemList</c> field on the bag.
    /// Equipped: index of the <c>_item</c> object-locator field on
    /// <c>EquipSlotElementSaveData</c>.
    /// </summary>
    public int ItemListFieldIdx { get; }

    /// <summary>
    /// Second descent step's <b>element index</b>.
    /// Inventory: item index inside the bag's <c>_itemList</c>.
    /// Equipped: always <c>0</c> — the path-ABI ignores
    /// <c>element_idx</c> for locator descents but the slot still
    /// has to be filled in.
    /// </summary>
    public int ItemIndex { get; }
    public int SocketListFieldIdx { get; }
    public int SocketIndex { get; }
    public int GemKeyFieldIdx { get; }
    public int EnduranceFieldIdx { get; }
    public int ValidSocketCountFieldIdx { get; }
    public int MaxSocketCount { get; }

    /// <summary>
    /// Whether the parent item can actually be worn — gamedata's
    /// <c>equip_type_info != 0</c>, i.e. it belongs to some equipment
    /// slot family. Drives
    /// <see cref="SocketEditorViewModel.EquippableOnly"/>.
    /// </summary>
    /// <remarks>
    /// <c>equip_type_info</c> is the right discriminator here and
    /// <c>use_socket</c> / <c>socket_valid_count</c> are the wrong ones:
    /// gamedata forbids sockets on rings, yet force-modding them works
    /// in-game, so a socket-capability test would drop items the user
    /// legitimately wants to edit. Measured on the maintainer's slot101,
    /// this splits the 1,543 socket-bearing rows into 754 wearable and
    /// 789 not — the latter being gold bars, cups, arrows, water,
    /// carrots, cooking oil and horse feed. Verified not to drop any
    /// wearable class: the only <c>equip_type_info == 0</c> items whose
    /// names look like gear are recipe books, notice papers and
    /// collectibles (<c>CraftingRecipe_*</c>, <c>NoticePaper_*</c>,
    /// <c>Collection_Prop_*</c>).
    /// <para>
    /// Defaults to <c>true</c> when iteminfo can't answer, so a missing
    /// gamedata bridge shows everything rather than an empty list.
    /// </para>
    /// </remarks>
    public bool IsEquippable { get; }

    public string BagLabel { get; }
    public uint ItemKey { get; }
    public string ItemName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(DisplayGemName))]
    [NotifyPropertyChangedFor(nameof(DisplayGemKeyText))]
    [NotifyCanExecuteChangedFor(nameof(ChangeGemCommand))]
    [NotifyCanExecuteChangedFor(nameof(FillGemCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearGemCommand))]
    private bool _isFilled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGemKeyText))]
    private uint _currentGemKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGemName))]
    private string _currentGemName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private byte _currentValidSocketCount;

    /// <summary>
    /// The full descent path down to the parent <c>ItemSaveData</c> —
    /// the only tuple that identifies an item uniquely.
    /// </summary>
    /// <remarks>
    /// <c>(BlockIndex, BagIndex, ItemIndex)</c> alone is <b>not</b>
    /// unique: a single top-level block can host several item lists,
    /// and the equipped list (<c>_list</c>) and the quick-use reserve
    /// list both address their first item as bag 0 / item 0. On the
    /// maintainer's slot101 that collapses 21 distinct items onto 10
    /// keys. Collapsing them is not cosmetic — it lets an Apply-Set
    /// write gems into an item the user never selected, and it lets a
    /// socket-count update on one item mark a *different* item as
    /// already-opened, which is exactly the state the engine rejects.
    /// Including both descent field indices makes the key collision-free.
    /// </remarks>
    public (int Block, int ListField, int Bag, int ItemField, int Item) ItemIdentity =>
        (BlockIndex, InventoryListFieldIdx, BagIndex, ItemListFieldIdx, ItemIndex);

    /// <summary>
    /// Whether the parent item's <c>_validSocketCount</c> field is
    /// <b>present</b> in the save. Absent is the game's encoding for
    /// "no socket ever opened" — it never writes an explicit 0 — so
    /// this is what decides between an in-place scalar write and an
    /// absent → present promotion when opening a slot. See
    /// <see cref="SocketEditorViewModel.TryEnsureSocketOpened"/>.
    /// </summary>
    [ObservableProperty]
    private bool _validSocketCountPresent;

    /// <summary>
    /// Set when this item's <c>_validSocketCount</c> was promoted
    /// absent → present <b>inside an open deferred batch</b>, which
    /// leaves its decoded byte range at <c>start == end == 0</c> until
    /// the commit re-decodes. While that holds, the in-place scalar
    /// setter rejects a write to it with <c>LENGTH_MISMATCH</c>, so
    /// <see cref="SocketEditorViewModel.TryEnsureSocketOpened"/> must
    /// raise the count through the presence surface instead.
    /// </summary>
    [ObservableProperty]
    private bool _validSocketCountRangeStale;

    [ObservableProperty]
    private uint? _appliedGemKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayGemName))]
    private string? _appliedGemName;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>
    /// Display string for the current-vs-applied gem state. Empty
    /// slots show <c>"(empty)"</c>. After a successful Apply, shows
    /// the applied gem name so the user can re-verify without
    /// scrolling between columns.
    /// </summary>
    public string DisplayGemName =>
        IsFilled
            ? (AppliedGemName ?? CurrentGemName)
            : "(empty)";

    /// <summary>Display string for the gem-key column — blank when empty.</summary>
    public string DisplayGemKeyText =>
        IsFilled
            ? CurrentGemKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";

    /// <summary>
    /// Concise status label: "Open", "Open (CE-bumped)", "Closed
    /// (gamedata)" — informational, drives the State column.
    /// </summary>
    public string StateLabel
    {
        get
        {
            var open = SocketIndex < CurrentValidSocketCount;
            if (IsFilled)
            {
                return open ? "Filled" : "Filled (slot was closed; bump on next edit)";
            }
            return open ? "Open" : "Closed (will open on Fill)";
        }
    }

    /// <summary>
    /// Internal: flip state to filled after a successful Apply so the
    /// row UI updates without reload + commands re-evaluate
    /// CanExecute.
    /// </summary>
    internal void SetFilled(uint gemKey, string gemName)
    {
        CurrentGemKey = gemKey;
        CurrentGemName = gemName;
        IsFilled = true;
    }

    /// <summary>Internal: flip state to empty after a successful Clear.</summary>
    internal void SetEmpty()
    {
        CurrentGemKey = 0;
        CurrentGemName = string.Empty;
        AppliedGemKey = null;
        AppliedGemName = null;
        IsFilled = false;
    }

    [RelayCommand(CanExecute = nameof(IsFilled))]
    private void ChangeGem() => _parent.RequestChangeGem(this);

    /// <summary>Open the gem picker to fill an empty slot.</summary>
    [RelayCommand(CanExecute = nameof(CanFill))]
    private void FillGem() => _parent.RequestChangeGem(this);

    private bool CanFill => !IsFilled;

    [RelayCommand(CanExecute = nameof(IsFilled))]
    private void ClearGem() => _parent.ApplyClear(this);
}

/// <summary>
/// Apply-Set target dropdown row — one entry per distinct item in
/// the editor. Used by <see cref="SocketEditorViewModel.SelectedTarget"/>
/// to route a gem-set apply to every socket of that one item.
/// </summary>
public sealed record GemSetTargetItem(
    int BlockIndex,
    int InventoryListFieldIdx,
    int BagIndex,
    int ItemListFieldIdx,
    int ItemIndex,
    string DisplayName,
    int MaxSocketCount)
{
    /// <summary>
    /// Same collision-free item key as
    /// <see cref="SocketRow.ItemIdentity"/> — routing an Apply-Set on
    /// the bare <c>(block, bag, item)</c> triple would splash the set
    /// onto a second, unrelated item sharing that triple.
    /// </summary>
    public (int Block, int ListField, int Bag, int ItemField, int Item) ItemIdentity =>
        (BlockIndex, InventoryListFieldIdx, BagIndex, ItemListFieldIdx, ItemIndex);
}

/// <summary>
/// Apply-Set "gem set" dropdown row. <see cref="DisplayName"/> is
/// pre-built at construction (resolved gem names joined with
/// commas) so the dropdown stays cheap to render. Holds the source
/// <see cref="GemSet"/> for the Apply path.
/// </summary>
public sealed record GemSetOption(
    string Label,
    IReadOnlyList<uint> GemKeys,
    string DisplayName)
{
    public static GemSetOption From(GemSet set, Services.LocalizationProvider localization)
    {
        // Resolve each gem key to a human-readable name. Falls back
        // to iteminfo string_key, then raw decimal — same shape the
        // Sockets editor's per-row gem label uses, so the dropdown
        // matches the column visually.
        var names = new string[set.GemKeys.Count];
        for (var i = 0; i < set.GemKeys.Count; i++)
        {
            var key = set.GemKeys[i];
            var resolved = localization.ResolveItemNameFormatted(key);
            if (string.IsNullOrEmpty(resolved))
            {
                resolved = localization.ItemInfoStringKey(key)
                           ?? key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            names[i] = resolved;
        }
        return new GemSetOption(
            set.Label, set.GemKeys,
            DisplayName: $"{set.Label} — {string.Join(" / ", names)}");
    }
}
