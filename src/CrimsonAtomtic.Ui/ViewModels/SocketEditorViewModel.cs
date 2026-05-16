using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// View-model for the Tools → Edit Item Sockets dialog. Walks every
/// <see cref="InventorySaveDataClass"/> block in the loaded save,
/// drills down through bags → items → socket lists, and surfaces one
/// row per socket slot (both <b>empty</b> and <b>filled</b>) for items
/// whose <c>_socketSaveDataList</c> is non-empty.
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
///     defaults to <see cref="DefaultGemEndurance"/> (max u16) so
///     greater (durability-bearing) gems start fresh.</item>
///   <item><b>Change</b> (filled → different gem): in-place
///     <see cref="ISaveLoader.SetScalarField"/> on <c>_itemKey</c> +
///     <c>_currentEndurance</c> reset to max (durability fix for
///     greater gems — v1 left the old slot's worn value in place).</item>
///   <item><b>Clear</b> (filled → empty): both fields demoted to
///     absent via <see cref="ISaveLoader.SetScalarFieldsPresentBatch"/>.</item>
/// </list>
/// Filling a slot whose index is past the current <c>_validSocketCount</c>
/// auto-bumps the count so the slot becomes visible in-game. Per the
/// user's request, the dialog lets you fill <i>any</i> slot up to the
/// underlying <c>_socketSaveDataList</c>'s actual capacity (the engine
/// pre-allocates 5 slots for socket-capable items) regardless of the
/// gamedata-defined limit, so CE-bypassed slots are accepted.
/// </para>
/// <para>
/// Out of scope: socket-count unlock for items that ship with
/// <c>_maxSocketCount = 0</c> (zero-record list). The predecessor
/// hard-warned "0→positive on a zero-record list may crash" because
/// it requires length-changing the list itself — different mutation
/// surface than the in-place fill v2 uses.
/// </para>
/// </remarks>
public sealed partial class SocketEditorViewModel : ObservableObject
{
    /// <summary>Class name of every top-level inventory block.</summary>
    private const string InventorySaveDataClass = "InventorySaveData";
    /// <summary>
    /// Class name of the top-level block carrying the player's
    /// equipped-gear slots. One per save. Each
    /// <c>_list[i]._item</c> is a full <see cref="ItemSaveData"/>
    /// child (object_locator) with the same socket/dye schema as
    /// inventory items — so we walk it here in parallel with
    /// <see cref="InventorySaveDataClass"/> rather than asking
    /// crimson-rs for a unified flat enumerator.
    /// </summary>
    private const string EquipmentSaveDataClass = "EquipmentSaveData";

    private const string InventoryListFieldName = "_inventorylist";
    private const string ItemListFieldName = "_itemList";
    /// <summary>
    /// Object-list field on <see cref="EquipmentSaveDataClass"/>
    /// holding 18 <c>EquipSlotElementSaveData</c> rows in the 1.07
    /// schema, each carrying an <c>_item</c> locator child.
    /// </summary>
    private const string EquipListFieldName = "_list";
    /// <summary>
    /// Object-locator field on each <c>EquipSlotElementSaveData</c>
    /// that wraps the equipped <c>ItemSaveData</c> child. Descent
    /// step uses <c>element_idx=0</c> per the path-ABI contract
    /// (ignored for locator descent).
    /// </summary>
    private const string EquipItemLocatorFieldName = "_item";
    private const string SocketListFieldName = "_socketSaveDataList";
    private const string ItemKeyFieldName = "_itemKey";

    /// <summary>
    /// Field name carrying the durability for greater (durability-
    /// bearing) gems. u16; we reset to <see cref="DefaultGemEndurance"/>
    /// on every Fill / Change so a fresh gem doesn't inherit the
    /// previous slot's worn value.
    /// </summary>
    private const string EnduranceFieldName = "_currentEndurance";

    /// <summary>
    /// u8 field on the parent <c>ItemSaveData</c> capturing how many of
    /// the slot list's entries are currently "open" (usable in-game).
    /// Filling a slot whose index is &gt;= this value auto-bumps it.
    /// </summary>
    private const string ValidSocketCountFieldName = "_validSocketCount";

    /// <summary>
    /// Sentinel "fresh gem" endurance. u16 max == 65535. Safe for
    /// both durability-bearing greater gems ("full durability") and
    /// no-durability gems (engine ignores the value). Conservative
    /// default until an upstream <c>iteminfo</c> getter for per-gem
    /// <c>max_endurance</c> ships.
    /// </summary>
    public const ushort DefaultGemEndurance = 0xFFFF;

    /// <summary>
    /// String-key prefixes that identify gem items in 1.06 iteminfo.
    /// "AbyssGear" is the engine-internal name for what's localized as
    /// "gem" in-game; gems split into stat-modifier gems
    /// (<c>Item_Stat_AbyssGear_*</c>) and skill-bestowing gems
    /// (<c>Item_Skill_AbyssGear_*</c>). 100% of the predecessor save
    /// editor's curated 189-entry gem list falls under one of these
    /// two prefixes in the 1.06 baseline dump.
    /// </summary>
    public static readonly IReadOnlyList<string> GemStringKeyPrefixes =
    [
        "Item_Stat_AbyssGear_",
        "Item_Skill_AbyssGear_",
    ];

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
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return $"{total} slot(s).";
            }
            return $"{Sockets.Count} of {total} slot(s) match.";
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
        if (string.IsNullOrWhiteSpace(needle))
        {
            foreach (var row in _allSockets)
            {
                Sockets.Add(row);
            }
            OnPropertyChanged(nameof(FilterCountText));
            return;
        }

        // Pass 1: collect item identities whose parent matches.
        var matchedItems = new HashSet<(int Block, int Bag, int Item)>();
        foreach (var row in _allSockets)
        {
            if (row.MatchesItemFilter(needle))
            {
                matchedItems.Add((row.BlockIndex, row.BagIndex, row.ItemIndex));
            }
        }

        // Pass 2: emit every row whose item matched OR whose gem matches.
        foreach (var row in _allSockets)
        {
            if (matchedItems.Contains((row.BlockIndex, row.BagIndex, row.ItemIndex))
                || row.MatchesSocketFilter(needle))
            {
                Sockets.Add(row);
            }
        }
        OnPropertyChanged(nameof(FilterCountText));
    }

    /// <summary>
    /// Distinct items present in the editor — drives the Apply-Set
    /// "target item" dropdown. Each entry collapses every SocketRow
    /// that belongs to the same physical item into a single picker
    /// option so the user picks an item, not a slot.
    /// </summary>
    public ObservableCollection<GemSetTargetItem> ApplySetTargets { get; } = new();

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
        IReadOnlyList<BlockSummary> blocks,
        IReadOnlyList<CustomGemSet>? customSets = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        var vm = new SocketEditorViewModel(loader, localization, journal, savePath);
        foreach (var b in blocks)
        {
            BlockDetails top;
            try
            {
                top = loader.LoadBlockDetails(savePath, b.Index);
            }
            catch (CrimsonSaveException)
            {
                continue;
            }
            if (string.Equals(b.ClassName, InventorySaveDataClass, StringComparison.Ordinal))
            {
                vm.CollectFromInventory(top, b.Index);
            }
            else if (string.Equals(b.ClassName, EquipmentSaveDataClass, StringComparison.Ordinal))
            {
                vm.CollectFromEquipment(top, b.Index);
            }
        }
        if (vm.Sockets.Count == 0)
        {
            return null;
        }
        vm.BuildApplySetState(customSets);
        var filledCount = 0;
        foreach (var r in vm.Sockets) if (r.IsFilled) filledCount++;
        vm.StatusMessage =
            $"{vm.Sockets.Count} slot(s) across {CountDistinctItems(vm.Sockets)} item(s) "
            + $"({filledCount} filled).";
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
        // Distinct items: collapse SocketRows sharing the same physical
        // (block, bag, item) tuple into one entry. Preserve insertion
        // order so the dropdown matches the user's mental scroll order
        // in the main DataGrid.
        var seen = new HashSet<(int Block, int Bag, int Item)>();
        foreach (var r in Sockets)
        {
            var triple = (r.BlockIndex, r.BagIndex, r.ItemIndex);
            if (seen.Add(triple))
            {
                ApplySetTargets.Add(new GemSetTargetItem(
                    r.BlockIndex, r.BagIndex, r.ItemIndex,
                    DisplayName: $"{r.BagLabel} · {r.ItemName} ({r.MaxSocketCount} slot{(r.MaxSocketCount == 1 ? "" : "s")})",
                    MaxSocketCount: r.MaxSocketCount));
            }
        }
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
        StatusMessage =
            $"Custom gem sets refreshed — {AvailableGemSets.Count} set(s) available in the Apply-Set dropdown.";
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
        var rows = new SortedDictionary<int, SocketRow>();
        foreach (var r in Sockets)
        {
            if (r.BlockIndex == target.BlockIndex
                && r.BagIndex == target.BagIndex
                && r.ItemIndex == target.ItemIndex)
            {
                rows[r.SocketIndex] = r;
            }
        }
        var applyCount = Math.Min(set.GemKeys.Count, target.MaxSocketCount);
        var changed = 0;
        for (var i = 0; i < applyCount; i++)
        {
            if (!rows.TryGetValue(i, out var row)) continue;
            var newKey = set.GemKeys[i];
            if (row.IsFilled && row.CurrentGemKey == newKey)
            {
                continue; // already what we want
            }
            ApplyGemPick(row, newKey);
            changed++;
        }
        if (changed == 0)
        {
            StatusMessage = $"Apply Set: {set.Label} — no changes (every targeted slot already matches).";
            return;
        }
        _journal.Log("Sockets",
            $"Applied set \"{set.Label}\" to {target.DisplayName} — {changed} slot(s) changed");
        StatusMessage =
            $"Applied set \"{set.Label}\" to {target.DisplayName}: {changed} slot(s) changed.";
    }

    private bool CanApplyGemSet =>
        SelectedTarget is not null && SelectedSet is not null;

    private static int CountDistinctItems(IEnumerable<SocketRow> rows)
    {
        var seen = new HashSet<(int Block, int Bag, int Item)>();
        foreach (var r in rows)
        {
            seen.Add((r.BlockIndex, r.BagIndex, r.ItemIndex));
        }
        return seen.Count;
    }

    private void CollectFromInventory(BlockDetails top, int blockIndex)
    {
        for (var f = 0; f < top.Fields.Count; f++)
        {
            var invList = top.Fields[f];
            if (!string.Equals(invList.Name, InventoryListFieldName, StringComparison.Ordinal)
                || invList.Elements is not { Count: > 0 } bags)
            {
                continue;
            }
            for (var bagIdx = 0; bagIdx < bags.Count; bagIdx++)
            {
                var bag = bags[bagIdx];
                for (var g = 0; g < bag.Fields.Count; g++)
                {
                    var itemListField = bag.Fields[g];
                    if (!string.Equals(itemListField.Name, ItemListFieldName, StringComparison.Ordinal)
                        || itemListField.Elements is not { Count: > 0 } items)
                    {
                        continue;
                    }
                    for (var itemIdx = 0; itemIdx < items.Count; itemIdx++)
                    {
                        CollectFromItem(
                            blockIndex,
                            firstStepFieldIdx: f,
                            firstStepElementIdx: bagIdx,
                            secondStepFieldIdx: g,
                            secondStepElementIdx: itemIdx,
                            item: items[itemIdx],
                            bagLabel: FormatBagLabel(_localization, bagIdx));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Collect socket rows from an <c>EquipmentSaveData</c> block.
    /// Layout: <c>EquipmentSaveData._list[i]._item</c> is an
    /// object_locator pointing at a full <c>ItemSaveData</c> child
    /// with the same socket schema as inventory items. Path to the
    /// item: <c>[(_list, slotIdx), (_item, 0)]</c> — same 2-step
    /// shape as the inventory path, so the reinterpreted indices
    /// (firstStep* = list+slot, secondStep* = locator+0) feed the
    /// existing path-construction code unchanged.
    /// </summary>
    private void CollectFromEquipment(BlockDetails top, int blockIndex)
    {
        for (var f = 0; f < top.Fields.Count; f++)
        {
            var listField = top.Fields[f];
            if (!string.Equals(listField.Name, EquipListFieldName, StringComparison.Ordinal)
                || listField.Elements is not { Count: > 0 } slots)
            {
                continue;
            }
            for (var slotIdx = 0; slotIdx < slots.Count; slotIdx++)
            {
                var slot = slots[slotIdx];
                for (var g = 0; g < slot.Fields.Count; g++)
                {
                    var itemLocator = slot.Fields[g];
                    if (!string.Equals(itemLocator.Name, EquipItemLocatorFieldName, StringComparison.Ordinal)
                        || !itemLocator.Present
                        || itemLocator.Child is not { } itemChild)
                    {
                        continue;
                    }
                    CollectFromItem(
                        blockIndex,
                        firstStepFieldIdx: f,
                        firstStepElementIdx: slotIdx,
                        secondStepFieldIdx: g,
                        secondStepElementIdx: 0,    // locator descent ignores element_idx
                        item: itemChild,
                        bagLabel: "Equipped");
                }
            }
        }
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
    public void ApplyGemPick(SocketRow row, uint newGemKey)
    {
        if (row.IsFilled && newGemKey == row.CurrentGemKey)
        {
            StatusMessage = "Same gem as current — no write performed.";
            return;
        }
        var pathToSocket = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
            new PathStep((uint)row.SocketListFieldIdx, (uint)row.SocketIndex),
        };
        var enduranceBytes = BitConverter.GetBytes(DefaultGemEndurance);
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
            MaybeBumpValidSocketCount(row);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = $"Apply failed ({row.BagLabel}, item {row.ItemIndex}, "
                + $"socket {row.SocketIndex}): {ex.Message}";
            row.LastError = ex.Message;
            return;
        }
        // Mirror state back so the row UI repaints and a follow-up
        // edit of the same slot routes through the "filled" branch
        // without a reload.
        var newGemName = FormatItemDisplay(_localization, newGemKey);
        var verb = row.IsFilled ? "Set" : "Filled";
        row.AppliedGemKey = newGemKey;
        row.AppliedGemName = newGemName;
        row.SetFilled(newGemKey, newGemName);
        row.LastError = null;
        IsDirty = true;
        _journal.Log("Sockets",
            $"{verb} gem in {row.ItemName} socket {row.SocketIndex} → {newGemName}");
        StatusMessage = $"{verb} gem in {row.ItemName} socket {row.SocketIndex}: "
            + $"→ {newGemName}.";
    }

    /// <summary>
    /// Clear a filled socket: both <c>_currentEndurance</c> +
    /// <c>_itemKey</c> are demoted to absent in one batch so the mask
    /// flips from <c>[0x03]</c> back to <c>[0x00]</c>. No-op on
    /// already-empty rows.
    /// </summary>
    internal void ApplyClear(SocketRow row)
    {
        if (!row.IsFilled)
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
            StatusMessage = $"Clear failed ({row.BagLabel}, item {row.ItemIndex}, "
                + $"socket {row.SocketIndex}): {ex.Message}";
            row.LastError = ex.Message;
            return;
        }
        var prevName = row.AppliedGemName ?? row.CurrentGemName;
        row.SetEmpty();
        row.LastError = null;
        IsDirty = true;
        _journal.Log("Sockets",
            $"Cleared gem in {row.ItemName} socket {row.SocketIndex} (was {prevName})");
        StatusMessage = $"Cleared gem in {row.ItemName} socket {row.SocketIndex} "
            + $"(was {prevName}).";
    }

    /// <summary>
    /// When the just-edited slot's index is &gt;= the parent item's
    /// current <c>_validSocketCount</c>, bump that count so the slot
    /// is visible in-game. No-op when the field is already absent
    /// from the schema (older items without the field) or the slot
    /// is within the existing window.
    /// </summary>
    private void MaybeBumpValidSocketCount(SocketRow row)
    {
        if (row.ValidSocketCountFieldIdx < 0)
        {
            return; // schema doesn't carry the field
        }
        var needed = (byte)Math.Min(byte.MaxValue, row.SocketIndex + 1);
        if (row.CurrentValidSocketCount >= needed)
        {
            return;
        }
        var pathToItem = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
        };
        try
        {
            _loader.SetScalarField(row.BlockIndex, pathToItem,
                row.ValidSocketCountFieldIdx, new[] { needed });
        }
        catch (CrimsonSaveException)
        {
            // Best-effort: the slot edit already landed; failing the
            // bump leaves the slot edited-but-invisible. Surface in
            // the row's error column but don't fail the parent flow.
            row.LastError = $"_validSocketCount bump failed; in-game may not show slot {row.SocketIndex}";
            return;
        }
        // Propagate the new value to every row of the same item so
        // subsequent edits don't re-bump.
        foreach (var r in Sockets)
        {
            if (r.BlockIndex == row.BlockIndex
                && r.BagIndex == row.BagIndex
                && r.ItemIndex == row.ItemIndex)
            {
                r.CurrentValidSocketCount = needed;
            }
        }
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
    /// Format a bag's position-in-inventorylist as a UI label. Uses the
    /// <see cref="LocalizationProvider.ResolveByFieldTypeName"/> "InventoryKey"
    /// table for friendly names where available; falls back to
    /// <c>"Bag N"</c> otherwise.
    /// </summary>
    private static string FormatBagLabel(LocalizationProvider localization, int bagIndex)
    {
        var label = localization.ResolveByFieldTypeName("InventoryKey", (uint)bagIndex);
        return string.IsNullOrEmpty(label) ? $"Bag {bagIndex}" : label;
    }

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
    int BagIndex,
    int ItemIndex,
    string DisplayName,
    int MaxSocketCount);

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
