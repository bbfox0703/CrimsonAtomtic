using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// VM for the per-item slot editor dialog. Lists every slot in one
/// item's <c>_itemDyeDataList</c>, exposes R/G/B/A + grime + palette
/// key + color-group key per slot with Apply.
///
/// <para>
/// Reads the current scalar values out of the cached BlockDetails
/// (one LoadBlockDetails roundtrip per child-dialog open). Writes
/// go through <see cref="ISaveLoader.SetScalarField"/> per scalar,
/// or <see cref="ISaveLoader.SetScalarFieldPresent"/> when promoting
/// an absent scalar to present (R/G/B/A typically start absent on
/// items the player hasn't dyed via the per-channel UI).
/// </para>
/// </summary>
public sealed partial class DyeSlotEditorViewModel : ObservableObject
{
    /// <summary>Class name of each element of <c>_itemDyeDataList</c>.</summary>
    public const string DyeElementClass = "ItemDyeSaveData";

    private readonly ISaveLoader _loader;
    private readonly LocalizationProvider _localization;
    private readonly ChangeJournal _journal;
    private readonly string _savePath;
    private readonly DyeEditorItemRow _itemRow;

    [ObservableProperty]
    private string? _statusMessage;

    public string HeaderText { get; }
    public string SubHeaderText { get; }

    public ObservableCollection<DyeSlotRow> Slots { get; } = [];

    /// <summary>Dropdown options for the material/palette picker.</summary>
    public ObservableCollection<DyeMaterialOption> MaterialOptions { get; } = [];

    /// <summary>Dropdown options for the color-group picker.</summary>
    public ObservableCollection<DyeColorGroupOption> ColorGroupOptions { get; } = [];

    public bool IsDirty { get; private set; }

    public DyeSlotEditorViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        ChangeJournal journal,
        string savePath,
        DyeEditorItemRow itemRow)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(itemRow);
        _loader = loader;
        _localization = localization;
        _journal = journal;
        _savePath = savePath;
        _itemRow = itemRow;

        HeaderText = $"Dye slots — {itemRow.ItemNameEnglish}";
        SubHeaderText = $"Bag: {itemRow.BagLabel}   ItemKey: {itemRow.ItemKey}";

        BuildDropdownOptions();
        LoadSlots();
    }

    private void BuildDropdownOptions()
    {
        // Color-group dropdown: full enumeration. ~10 rows in 1.07.
        if (_localization.DyeColorGroupInfo is { } cg)
        {
            for (var i = 0; i < cg.EntryCount; i++)
            {
                var entry = cg.GetEntry(i);
                if (entry is not { } e) continue;
                ColorGroupOptions.Add(new DyeColorGroupOption(e.Key, e.Name));
            }
        }
        // Material/palette dropdown: enumerate, label each with its
        // first sub-record's material name so the user sees something
        // meaningful ("0 — cloth" / "1 — leather" / …). 11 rows in 1.07.
        if (_localization.DyeTexturePalleteInfo is { } pal)
        {
            for (var i = 0; i < pal.EntryCount; i++)
            {
                var keyN = pal.GetEntryKey(i);
                if (keyN is not { } key) continue;
                var subCount = pal.LookupSubCount(key) ?? 0;
                var primaryMat = subCount > 0
                    ? (pal.LookupSubMaterialName(key, 0) ?? string.Empty)
                    : string.Empty;
                MaterialOptions.Add(new DyeMaterialOption(key, primaryMat, subCount));
            }
        }
    }

    private void LoadSlots()
    {
        Slots.Clear();
        BlockDetails details;
        try
        {
            details = _loader.LoadBlockDetails(_savePath, _itemRow.BlockIndex);
        }
        catch (CrimsonSaveException ex)
        {
            StatusMessage = UiText.Format("DyeReadFailed", "Failed to read item: {0}", ex.Message);
            return;
        }

        // Generic two-step descent driven by the row's stored
        // first/second-step indices. Works uniformly for inventory
        // items (_inventorylist → _itemList) and equipped gear
        // (_list → _item locator).
        if (!TryDescend(details, _itemRow.FirstStepFieldIndex,
                        _itemRow.FirstStepElementIndex, out var afterStep1))
        {
            StatusMessage = UiText.Get("DyeItemNotFound", "Item not found (schema drift?).");
            return;
        }
        if (!TryDescend(afterStep1, _itemRow.SecondStepFieldIndex,
                        _itemRow.SecondStepElementIndex, out var item))
        {
            StatusMessage = UiText.Get("DyeItemNotFound", "Item not found (schema drift?).");
            return;
        }
        var dyeListField = item.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, DyeEditorViewModel.DyeListFieldName, StringComparison.Ordinal));
        if (dyeListField?.Elements is not { Count: > 0 } dyeSlots)
        {
            StatusMessage = UiText.Get("DyeNoSlots", "This item has no dye slots.");
            return;
        }

        // First-element schema drives the per-element field indices.
        // We assume the rest of the list shares the schema (true in
        // practice — all elements of an ObjectList are the same class).
        var first = dyeSlots[0];
        if (!string.Equals(first.ClassName, DyeElementClass, StringComparison.Ordinal))
        {
            StatusMessage = UiText.Format("DyeUnexpectedClass", "Unexpected dye element class: {0}", first.ClassName);
            return;
        }
        var fieldIdx = ResolveFieldIndices(first.Fields);

        for (var i = 0; i < dyeSlots.Count; i++)
        {
            Slots.Add(DyeSlotRow.From(
                this, i, dyeSlots[i], fieldIdx,
                _localization, _itemRow));
        }
        StatusMessage = UiText.Format("DyeSlotsLoaded", "{0} dye slot(s) loaded.", Slots.Count);
    }

    private static DyeFieldIndices ResolveFieldIndices(IReadOnlyList<DecodedFieldRow> fields)
    {
        var ix = new DyeFieldIndices();
        foreach (var f in fields)
        {
            switch (f.Name)
            {
                case "_dyeSlotNo":            ix.SlotNo = f.FieldIndex; break;
                case "_dyeColorR":            ix.R = f.FieldIndex; break;
                case "_dyeColorG":            ix.G = f.FieldIndex; break;
                case "_dyeColorB":            ix.B = f.FieldIndex; break;
                case "_dyeColorA":            ix.A = f.FieldIndex; break;
                case "_grimeOpacity":         ix.Grime = f.FieldIndex; break;
                case "_dyeColorGroupInfoKey": ix.ColorGroup = f.FieldIndex; break;
                case "_texturePalleteKey":    ix.Material = f.FieldIndex; break;
                case "_disableSymbol":        ix.DisableSymbol = f.FieldIndex; break;
            }
        }
        return ix;
    }

    private static DecodedFieldRow? FindField(IReadOnlyList<DecodedFieldRow> fields, string name)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.Name, name, StringComparison.Ordinal))
            {
                return f;
            }
        }
        return null;
    }

    /// <summary>
    /// Descend one step into <paramref name="parent"/>: through field
    /// <paramref name="fieldIdx"/>, then through its
    /// <paramref name="elementIdx"/>-th list element (ObjectList) or
    /// through its inline child (object_locator — <paramref name="elementIdx"/>
    /// ignored). Returns the resolved nested <see cref="BlockDetails"/>
    /// on success; <c>false</c> when the field is absent / out of
    /// range / unsupported shape.
    /// </summary>
    private static bool TryDescend(
        BlockDetails parent, uint fieldIdx, uint elementIdx,
        out BlockDetails nested)
    {
        nested = null!;
        if (fieldIdx >= parent.Fields.Count)
        {
            return false;
        }
        var f = parent.Fields[(int)fieldIdx];
        if (!f.Present)
        {
            return false;
        }
        if (f.Elements is { Count: > 0 } elements)
        {
            if (elementIdx >= elements.Count) return false;
            nested = elements[(int)elementIdx];
            return true;
        }
        if (f.Child is { } child)
        {
            nested = child;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Aggregate journal log per Apply (one entry per slot, listing
    /// which scalars flipped — better than per-scalar spam).
    /// </summary>
    internal void LogSlotApplied(DyeSlotRow row, IReadOnlyList<string> changedLabels)
    {
        if (changedLabels.Count == 0) return;
        _journal.Log(UiText.Get("JournalCatDye", "Dye"),
            UiText.Format("JournalDyeEdited", "Edited dye on {0} slot {1} ({2})",
                _itemRow.ItemNameEnglish, row.SlotIndex, string.Join(", ", changedLabels)));
    }

    /// <summary>
    /// Expose <see cref="ISaveLoader.RunDeferred"/> to the per-row
    /// <see cref="DyeSlotRow"/> so its Apply command can wrap a batch
    /// of scalar writes in one deferred-redecode pass. Kept as a thin
    /// pass-through so the loader instance itself stays private to
    /// the parent VM.
    /// </summary>
    internal void LoaderRunDeferred(Action body) => _loader.RunDeferred(body);

    /// <summary>
    /// Raised when a row's "Pick…" button is clicked. The hosting
    /// window's code-behind subscribes and opens the modal palette
    /// picker dialog, then calls <see cref="DyeSlotRow.ApplyPickedColor"/>
    /// on the row when the user commits a selection.
    /// </summary>
    public event Action<DyeSlotRow>? PickColorRequested;

    internal void RequestPickColor(DyeSlotRow row) => PickColorRequested?.Invoke(row);

    /// <summary>
    /// Resolve the dye color-group catalog needed by the picker. Returns
    /// null when characterinfo / dye gamedata isn't loaded (offline
    /// fallback — the picker UI then disables itself rather than
    /// crashing).
    /// </summary>
    public NativeDyeColorGroupInfoCatalog? DyeColorGroupCatalog =>
        _localization.DyeColorGroupInfo;

    /// <summary>
    /// Apply one scalar mutation against a specific dye-slot scalar
    /// field. Path: <c>(blockIdx, [(invField, invIdx), (itemField,
    /// itemIdx), (dyeField, slotIdx)], scalarField)</c>. Promotes
    /// absent → present when needed via <c>SetScalarFieldPresent</c>;
    /// otherwise patches in place via <c>SetScalarField</c>.
    /// </summary>
    internal bool TryWriteScalar(int slotIndex, int scalarFieldIndex,
                                 bool wasPresent, ReadOnlySpan<byte> bytes,
                                 out string errorMessage)
    {
        errorMessage = string.Empty;
        // Path = item-path + (dyeList, slotIdx). Item-path is the
        // first/second-step pair the row carries (inventory descent or
        // equipment descent, transparent to this writer).
        var itemPath = _itemRow.BuildPathToItem();
        var path = new PathStep[itemPath.Length + 1];
        Array.Copy(itemPath, path, itemPath.Length);
        path[^1] = new PathStep(_itemRow.DyeListFieldIndex, (uint)slotIndex);
        try
        {
            if (!wasPresent)
            {
                _loader.SetScalarFieldPresent(
                    _itemRow.BlockIndex, path, scalarFieldIndex,
                    makePresent: true, initialBytes: bytes);
            }
            else
            {
                _loader.SetScalarField(
                    _itemRow.BlockIndex, path, scalarFieldIndex, bytes);
            }
        }
        catch (CrimsonSaveException ex)
        {
            errorMessage = $"{ex.Message} (code {ex.ErrorCode})";
            return false;
        }
        IsDirty = true;
        return true;
    }
}

/// <summary>Field-index cache for one ItemDyeSaveData element.</summary>
internal struct DyeFieldIndices
{
    public int SlotNo;
    public int R;
    public int G;
    public int B;
    public int A;
    public int Grime;
    public int ColorGroup;
    public int Material;
    public int DisableSymbol;

    public DyeFieldIndices()
    {
        SlotNo = R = G = B = A = Grime = ColorGroup = Material = DisableSymbol = -1;
    }
}

/// <summary>
/// One dye-slot row in the per-item editor. Holds editable values
/// for R/G/B/A + grime + material + color group; the Apply command
/// pushes any modified-from-original values to the save via the
/// parent VM's per-scalar writer.
/// </summary>
public sealed partial class DyeSlotRow : ObservableObject
{
    private readonly DyeSlotEditorViewModel _parent;
    private readonly DyeFieldIndices _fields;
    private readonly bool _origRPresent, _origGPresent, _origBPresent, _origAPresent;
    private readonly bool _origGrimePresent, _origColorGroupPresent, _origMaterialPresent;
    private readonly byte _origR, _origG, _origB, _origA;
    private readonly sbyte _origGrime;
    private readonly uint _origColorGroupKey;
    private readonly ushort _origMaterialKey;

    public int SlotIndex { get; }
    public sbyte SlotNo { get; }
    public string SlotLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchBrush))]
    [NotifyPropertyChangedFor(nameof(SwatchHex))]
    private byte _r;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchBrush))]
    [NotifyPropertyChangedFor(nameof(SwatchHex))]
    private byte _g;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchBrush))]
    [NotifyPropertyChangedFor(nameof(SwatchHex))]
    private byte _b;

    [ObservableProperty] private byte _a;
    [ObservableProperty] private sbyte _grime;

    /// <summary>
    /// Solid color brush for the per-row swatch in the editor's "Color"
    /// column. Alpha forced to 0xFF for visibility — the underlying
    /// <see cref="A"/> field is preserved separately and written
    /// through Apply unchanged.
    /// </summary>
    public IBrush SwatchBrush => new SolidColorBrush(Color.FromRgb(R, G, B));

    /// <summary>Hex preview "#rrggbb" — drives the swatch tooltip.</summary>
    public string SwatchHex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Currently-selected material option. <c>null</c> when the slot's
    /// palette key isn't in the dropdown options (offline / gamedata
    /// unavailable) or the field is absent and the user hasn't picked
    /// one yet.
    /// </summary>
    [ObservableProperty] private DyeMaterialOption? _selectedMaterial;

    [ObservableProperty] private DyeColorGroupOption? _selectedColorGroup;

    [ObservableProperty] private string? _lastError;

    public ObservableCollection<DyeMaterialOption> MaterialOptions => _parent.MaterialOptions;
    public ObservableCollection<DyeColorGroupOption> ColorGroupOptions => _parent.ColorGroupOptions;

    private DyeSlotRow(
        DyeSlotEditorViewModel parent, int slotIndex, sbyte slotNo,
        DyeFieldIndices fields,
        byte r, byte g, byte b, byte a, sbyte grime,
        uint colorGroupKey, ushort materialKey,
        bool rPresent, bool gPresent, bool bPresent, bool aPresent,
        bool grimePresent, bool colorGroupPresent, bool materialPresent,
        DyeColorGroupOption? selectedColorGroup,
        DyeMaterialOption? selectedMaterial)
    {
        _parent = parent;
        _fields = fields;
        SlotIndex = slotIndex;
        SlotNo = slotNo;
        SlotLabel = slotNo >= 0 ? $"Slot {slotNo}" : $"Slot #{slotIndex} (no _dyeSlotNo)";
        _r = r; _g = g; _b = b; _a = a; _grime = grime;
        _origR = r; _origG = g; _origB = b; _origA = a; _origGrime = grime;
        _origColorGroupKey = colorGroupKey;
        _origMaterialKey = materialKey;
        _origRPresent = rPresent;
        _origGPresent = gPresent;
        _origBPresent = bPresent;
        _origAPresent = aPresent;
        _origGrimePresent = grimePresent;
        _origColorGroupPresent = colorGroupPresent;
        _origMaterialPresent = materialPresent;
        _selectedColorGroup = selectedColorGroup;
        _selectedMaterial = selectedMaterial;
    }

    internal static DyeSlotRow From(
        DyeSlotEditorViewModel parent, int slotIndex,
        BlockDetails slotBlock, DyeFieldIndices fields,
        LocalizationProvider localization, DyeEditorItemRow itemRow)
    {
        // Scalar reads with defaults — absent → 0 (or sentinel) so
        // the UI shows a sensible starting value the user can edit.
        sbyte slotNo = -1;
        byte r = 0, g = 0, b = 0, a = 0;
        sbyte grime = 0;
        uint colorGroupKey = 0;
        ushort materialKey = 0;
        bool rPres = false, gPres = false, bPres = false, aPres = false;
        bool grimePres = false, cgPres = false, matPres = false;
        foreach (var f in slotBlock.Fields)
        {
            switch (f.Name)
            {
                case "_dyeSlotNo":
                    if (f.Present && TryParseI8(f.Value, out var sn)) slotNo = sn;
                    break;
                case "_dyeColorR":
                    if (f.Present && TryParseU8(f.Value, out var rv)) { r = rv; rPres = true; }
                    break;
                case "_dyeColorG":
                    if (f.Present && TryParseU8(f.Value, out var gv)) { g = gv; gPres = true; }
                    break;
                case "_dyeColorB":
                    if (f.Present && TryParseU8(f.Value, out var bv)) { b = bv; bPres = true; }
                    break;
                case "_dyeColorA":
                    if (f.Present && TryParseU8(f.Value, out var av)) { a = av; aPres = true; }
                    break;
                case "_grimeOpacity":
                    if (f.Present && TryParseI8(f.Value, out var grv)) { grime = grv; grimePres = true; }
                    break;
                case "_dyeColorGroupInfoKey":
                    if (f.Present && TryParseU32(f.Value, out var cgv)) { colorGroupKey = cgv; cgPres = true; }
                    break;
                case "_texturePalleteKey":
                    if (f.Present && TryParseU16(f.Value, out var mkv)) { materialKey = mkv; matPres = true; }
                    break;
            }
        }
        // Look up matching dropdown options for the current keys.
        DyeColorGroupOption? selCg = null;
        if (cgPres)
        {
            foreach (var opt in parent.ColorGroupOptions)
            {
                if (opt.Key == colorGroupKey) { selCg = opt; break; }
            }
        }
        DyeMaterialOption? selMat = null;
        if (matPres)
        {
            foreach (var opt in parent.MaterialOptions)
            {
                if (opt.Key == materialKey) { selMat = opt; break; }
            }
        }
        return new DyeSlotRow(parent, slotIndex, slotNo, fields,
            r, g, b, a, grime, colorGroupKey, materialKey,
            rPres, gPres, bPres, aPres, grimePres, cgPres, matPres,
            selCg, selMat);
    }

    /// <summary>
    /// Fired when the user clicks the per-row "Pick…" button. The
    /// editor window's code-behind handles the event by opening
    /// <c>DyePalettePickerWindow</c> modally; on close the chosen
    /// RGB lands back here via <see cref="ApplyPickedColor"/>.
    /// </summary>
    [RelayCommand]
    private void RequestPickColor() => _parent.RequestPickColor(this);

    /// <summary>
    /// Called by the editor window's code-behind after the modal
    /// picker dialog closed with a chosen RGB. Updates the row's
    /// editable <see cref="R"/> / <see cref="G"/> / <see cref="B"/>
    /// (the <see cref="A"/> alpha + <see cref="SelectedColorGroup"/>
    /// theme are left untouched — palette positions are always
    /// alpha=0xFF per vendor docs and the theme picker is a separate
    /// dropdown). The user still needs to click Apply on this row
    /// to persist the change.
    /// </summary>
    internal void ApplyPickedColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        // Defensive: if A is currently absent or 0, normalize to 0xFF
        // since every palette position uses opaque alpha in-game.
        if (A == 0) A = 0xFF;
    }

    [RelayCommand]
    private void Apply()
    {
        LastError = null;
        var writes = new List<(int FieldIdx, bool WasPresent, byte[] Bytes, string Label)>();

        if (_fields.R >= 0 && (R != _origR || !_origRPresent))
            writes.Add((_fields.R, _origRPresent, [R], "R"));
        if (_fields.G >= 0 && (G != _origG || !_origGPresent))
            writes.Add((_fields.G, _origGPresent, [G], "G"));
        if (_fields.B >= 0 && (B != _origB || !_origBPresent))
            writes.Add((_fields.B, _origBPresent, [B], "B"));
        if (_fields.A >= 0 && (A != _origA || !_origAPresent))
            writes.Add((_fields.A, _origAPresent, [A], "A"));
        if (_fields.Grime >= 0 && (Grime != _origGrime || !_origGrimePresent))
            writes.Add((_fields.Grime, _origGrimePresent, [(byte)Grime], "Grime"));

        if (_fields.ColorGroup >= 0 && SelectedColorGroup is { } cg
            && (cg.Key != _origColorGroupKey || !_origColorGroupPresent))
        {
            writes.Add((_fields.ColorGroup, _origColorGroupPresent,
                       BitConverter.GetBytes(cg.Key), "ColorGroup"));
        }
        if (_fields.Material >= 0 && SelectedMaterial is { } mat
            && ((ushort)mat.Key != _origMaterialKey || !_origMaterialPresent))
        {
            writes.Add((_fields.Material, _origMaterialPresent,
                       BitConverter.GetBytes((ushort)mat.Key), "Material"));
        }

        if (writes.Count == 0)
        {
            LastError = "No pending changes.";
            return;
        }

        var appliedLabels = new List<string>(writes.Count);
        // Wrap the per-Apply writes in a deferred-redecode batch (see
        // vendor/crimson-rs/docs/save-deferred-redecode.md). Up to 7
        // scalar mutations land per Apply; any absent→present
        // transition (SetScalarFieldPresent) is length-changing and
        // would otherwise trigger one full body re-decode per call
        // (~25ms on a 5MB body). With the batch every write stays in
        // the in-memory tree and the trailing commit does ONE encode +
        // parse + decode pass.
        //
        // TryWriteScalar catches CrimsonSaveException internally,
        // surfaces the error via the out parameter, and we break out of
        // the foreach on first failure. RunDeferred sees the normal
        // return and commits whatever already landed (matches the
        // pre-batch partial-success behaviour where the writes that
        // succeeded before the failure stayed). A commit-time
        // MUTATION_INVALID is caught at the outer level and surfaced.
        try
        {
            _parent.LoaderRunDeferred(() =>
            {
                foreach (var w in writes)
                {
                    if (!_parent.TryWriteScalar(SlotIndex, w.FieldIdx, w.WasPresent, w.Bytes,
                                                out var err))
                    {
                        LastError = $"{w.Label}: {err}";
                        return;
                    }
                    appliedLabels.Add(w.Label);
                }
            });
        }
        catch (CrimsonSaveException commitEx)
        {
            LastError = $"Commit failed: {commitEx.Message} (code {commitEx.ErrorCode}). "
                + "Reload the save without writing to revert.";
            return;
        }
        if (LastError is not null && appliedLabels.Count == 0)
        {
            // First write failed pre-commit — LastError already set to
            // "<Label>: <err>". Leave it as-is.
            return;
        }
        // Single per-slot journal entry aggregates which scalars
        // flipped, so the user's change list reads "Edited dye on
        // Robe slot 1 (R,G,B,Material)" not 4 separate lines.
        _parent.LogSlotApplied(this, appliedLabels);
        LastError = $"Applied: {string.Join(", ", appliedLabels)}.";
    }

    // ── Helpers: parse pre-formatted "<value> <tag>" scalar strings ──────────

    private static bool TryParseU8(string formatted, out byte value)
    {
        value = 0;
        return ScalarFieldEditing.TryParse(formatted, out var raw, out var tag)
            && tag == "u8"
            && byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private static bool TryParseI8(string formatted, out sbyte value)
    {
        value = 0;
        return ScalarFieldEditing.TryParse(formatted, out var raw, out var tag)
            && tag == "i8"
            && sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private static bool TryParseU16(string formatted, out ushort value)
    {
        value = 0;
        return ScalarFieldEditing.TryParse(formatted, out var raw, out var tag)
            && tag == "u16"
            && ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private static bool TryParseU32(string formatted, out uint value)
    {
        value = 0;
        return ScalarFieldEditing.TryParse(formatted, out var raw, out var tag)
            && tag == "u32"
            && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Dropdown option for the material/palette picker.</summary>
public sealed record DyeMaterialOption(uint Key, string PrimaryMaterialName, int SubCount)
{
    public string Display => string.IsNullOrEmpty(PrimaryMaterialName)
        ? $"{Key}"
        : $"{Key} — {PrimaryMaterialName} ({SubCount} sub)";
}

/// <summary>Dropdown option for the color-group picker.</summary>
public sealed record DyeColorGroupOption(uint Key, string Name)
{
    public string Display => string.IsNullOrEmpty(Name) ? $"0x{Key:X8}" : Name;
}
