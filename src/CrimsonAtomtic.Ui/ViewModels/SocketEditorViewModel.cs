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
/// row per <b>filled</b> socket (mask bit set, <c>_itemKey</c> present).
/// </summary>
/// <remarks>
/// <para>
/// v1 scope: <b>swap-only</b>. Per-row <c>Change Gem…</c> opens a
/// gem-filtered <see cref="ItemPickerViewModel"/>; the picked
/// <see cref="ItemPickerRow.ItemKey"/> is written in-place to the
/// socket element's <c>_itemKey</c> via <see cref="ISaveLoader.SetScalarField"/>.
/// </para>
/// <para>
/// Out of scope (per the predecessor's UI hard-warnings):
/// <list type="bullet">
///   <item>Fill empty socket — embedding requires the in-game Witch NPC first; forcing fills can crash.</item>
///   <item>Clear filled socket — length-changing splice that needs careful coupled-write bookkeeping.</item>
///   <item>Socket-count unlock — couples <c>_maxSocketCount</c> + <c>_validSocketCount</c> + <c>_endurance</c> high byte; "0→positive on a zero-record list may crash" per the predecessor.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class SocketEditorViewModel : ObservableObject
{
    /// <summary>Class name of every top-level inventory block.</summary>
    private const string InventorySaveDataClass = "InventorySaveData";

    private const string InventoryListFieldName = "_inventorylist";
    private const string ItemListFieldName = "_itemList";
    private const string SocketListFieldName = "_socketSaveDataList";
    private const string ItemKeyFieldName = "_itemKey";

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

    public ObservableCollection<SocketRow> Sockets { get; } = new();

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
        string savePath)
    {
        _loader = loader;
        _localization = localization;
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
        string savePath,
        IReadOnlyList<BlockSummary> blocks)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        var vm = new SocketEditorViewModel(loader, localization, savePath);
        foreach (var b in blocks)
        {
            if (!string.Equals(b.ClassName, InventorySaveDataClass, StringComparison.Ordinal))
            {
                continue;
            }
            BlockDetails top;
            try
            {
                top = loader.LoadBlockDetails(savePath, b.Index);
            }
            catch (CrimsonSaveException)
            {
                continue;
            }
            vm.CollectFromInventory(top, b.Index);
        }
        if (vm.Sockets.Count == 0)
        {
            return null;
        }
        vm.StatusMessage = $"{vm.Sockets.Count} filled socket(s) across "
            + $"{CountDistinctItems(vm.Sockets)} item(s).";
        return vm;
    }

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
                            inventoryListFieldIdx: f,
                            bagIndex: bagIdx,
                            itemListFieldIdx: g,
                            itemIndex: itemIdx,
                            item: items[itemIdx]);
                    }
                }
            }
        }
    }

    private void CollectFromItem(
        int blockIndex,
        int inventoryListFieldIdx,
        int bagIndex,
        int itemListFieldIdx,
        int itemIndex,
        BlockDetails item)
    {
        uint itemKey = 0;
        DecodedFieldRow? socketListField = null;
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
        }
        if (socketListField?.Elements is not { Count: > 0 } sockets)
        {
            return;
        }
        var itemName = FormatItemDisplay(_localization, itemKey);
        for (var s = 0; s < sockets.Count; s++)
        {
            var socket = sockets[s];
            // Surface only filled sockets — empty sockets aren't
            // user-fillable per the safe-edit contract.
            DecodedFieldRow? gemKeyField = null;
            foreach (var sf in socket.Fields)
            {
                if (string.Equals(sf.Name, ItemKeyFieldName, StringComparison.Ordinal))
                {
                    gemKeyField = sf;
                    break;
                }
            }
            if (gemKeyField is null
                || !gemKeyField.Present
                || !TryParseScalarUInt(gemKeyField.Value, out var gemKeyU64)
                || gemKeyU64 == 0
                || gemKeyU64 > uint.MaxValue)
            {
                continue;
            }
            var gemKey = (uint)gemKeyU64;
            var gemName = FormatItemDisplay(_localization, gemKey);
            var row = new SocketRow(
                vm: this,
                blockIndex: blockIndex,
                inventoryListFieldIdx: inventoryListFieldIdx,
                bagIndex: bagIndex,
                itemListFieldIdx: itemListFieldIdx,
                itemIndex: itemIndex,
                socketListFieldIdx: socketListField.FieldIndex,
                socketIndex: s,
                gemKeyFieldIdx: gemKeyField.FieldIndex,
                bagLabel: FormatBagLabel(_localization, bagIndex),
                itemKey: itemKey,
                itemName: itemName,
                currentGemKey: gemKey,
                currentGemName: gemName);
            Sockets.Add(row);
        }
    }

    /// <summary>
    /// Called by <see cref="SocketRow.ChangeGemCommand"/> — forwards to
    /// the dialog code-behind to open a gem-filtered Item Picker.
    /// </summary>
    internal void RequestChangeGem(SocketRow row) => ChangeGemRequested?.Invoke(row);

    /// <summary>
    /// Apply a user-picked gem to <paramref name="row"/>. Writes the
    /// new gem ItemKey into the socket element's <c>_itemKey</c> field
    /// via <see cref="ISaveLoader.SetScalarField"/>.
    /// </summary>
    public void ApplyGemPick(SocketRow row, uint newGemKey)
    {
        if (newGemKey == row.CurrentGemKey)
        {
            StatusMessage = "Same gem as current — no write performed.";
            return;
        }
        var path = new[]
        {
            new PathStep((uint)row.InventoryListFieldIdx, (uint)row.BagIndex),
            new PathStep((uint)row.ItemListFieldIdx, (uint)row.ItemIndex),
            new PathStep((uint)row.SocketListFieldIdx, (uint)row.SocketIndex),
        };
        var bytes = BitConverter.GetBytes(newGemKey);
        try
        {
            _loader.SetScalarField(row.BlockIndex, path, row.GemKeyFieldIdx, bytes);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = $"Apply failed (bag {row.BagIndex}, item {row.ItemIndex}, "
                + $"socket {row.SocketIndex}): {ex.Message}";
            row.LastError = ex.Message;
            return;
        }
        row.AppliedGemKey = newGemKey;
        row.AppliedGemName = FormatItemDisplay(_localization, newGemKey);
        row.LastError = null;
        IsDirty = true;
        StatusMessage = $"Swapped gem in {row.ItemName} socket {row.SocketIndex}: "
            + $"{row.CurrentGemName} → {row.AppliedGemName}.";
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
        return localization.ItemInfoStringKey(itemKey) ?? itemKey.ToString();
    }

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
/// One filled socket slot row in the Sockets editor dialog.
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
        string bagLabel,
        uint itemKey,
        string itemName,
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
        BagLabel = bagLabel;
        ItemKey = itemKey;
        ItemName = itemName;
        CurrentGemKey = currentGemKey;
        CurrentGemName = currentGemName;
    }

    public int BlockIndex { get; }
    public int InventoryListFieldIdx { get; }
    public int BagIndex { get; }
    public int ItemListFieldIdx { get; }
    public int ItemIndex { get; }
    public int SocketListFieldIdx { get; }
    public int SocketIndex { get; }
    public int GemKeyFieldIdx { get; }

    public string BagLabel { get; }
    public uint ItemKey { get; }
    public string ItemName { get; }
    public uint CurrentGemKey { get; }
    public string CurrentGemName { get; }

    [ObservableProperty]
    private uint? _appliedGemKey;

    [ObservableProperty]
    private string? _appliedGemName;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>
    /// Display string for the current-vs-applied gem state. After a
    /// successful Apply, shows the applied gem name so the user can
    /// re-verify without scrolling between columns.
    /// </summary>
    public string DisplayGemName => AppliedGemName ?? CurrentGemName;

    partial void OnAppliedGemNameChanged(string? value) =>
        OnPropertyChanged(nameof(DisplayGemName));

    [RelayCommand]
    private void ChangeGem() => _parent.RequestChangeGem(this);
}
