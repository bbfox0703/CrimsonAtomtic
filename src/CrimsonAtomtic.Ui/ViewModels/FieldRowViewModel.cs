using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Mutable wrapper around an immutable <see cref="DecodedFieldRow"/> for
/// the field-detail DataGrid. Holds the in-progress edit text, the
/// validation error (if any), and — for nested scalars — the descent
/// path from the top-level block, so cell editing can stay reactive
/// without rebuilding the row record.
/// </summary>
public sealed partial class FieldRowViewModel : ObservableObject
{
    private readonly DecodedFieldRow _row;
    private readonly string _typeTag;
    private string _committedRawText;
    /// <summary>
    /// Held so <see cref="ApplyCommittedValue"/> can refresh
    /// <see cref="ResolvedName"/> when the underlying raw value
    /// changes — otherwise editing e.g. <c>_itemKey</c> updates the
    /// Value column but leaves the Name column showing the
    /// *previous* item's name until the user navigates away and
    /// back.
    /// </summary>
    private readonly LocalizationProvider? _localization;

    /// <summary>
    /// Descent path from the top-level TOC block to this row's enclosing
    /// block. Empty for top-level rows; non-empty when the row sits
    /// inside a locator child or list element. Mutations push this path
    /// through the C ABI's <c>set_scalar_field_path</c>.
    /// </summary>
    public IReadOnlyList<PathStep> EnclosingPath { get; }

    public FieldRowViewModel(
        DecodedFieldRow row,
        IReadOnlyList<PathStep> enclosingPath,
        LocalizationProvider? localization = null)
    {
        _row = row;
        _displayValue = row.Value;
        _present = row.Present;
        _localization = localization;
        EnclosingPath = enclosingPath;

        // Three editable shapes:
        //   1. Present scalar — parse "raw <tag>" from row.Value.
        //   2. Absent scalar (schema fixed_prefix / fixed_suffix, MetaKind 0
        //      or 2) — Rust erases the underlying kind to Absent on the
        //      JSON side, so we infer the editable tag from the schema
        //      TypeName instead. Apply routes to SetScalarFieldPresent.
        //   3. Everything else — locators, lists, vectors, bytes — not
        //      reachable through this textbox; IsEditable stays false.
        if (ScalarFieldEditing.IsScalarKind(row.Kind)
            && ScalarFieldEditing.TryParse(row.Value, out var raw, out var tag)
            && ScalarFieldEditing.SupportedTypeTags.Contains(tag))
        {
            _typeTag = tag;
            _committedRawText = raw;
            _rawText = raw;
            IsEditable = true;

            // Name resolution. Restricted to schema-typed key fields
            // (ItemKey / FactionKey / CharacterKey / GimmickInfoKey /
            // LevelGimmickSceneObjectInfoKey are u32; InventoryKey is
            // u16 — see LocalizationProvider.TypeNameToTypeByte +
            // InventoryContainerLabels) to avoid false positives:
            // every save has dozens of small-integer scalars (slot
            // counts, raw indices, etc.) that would coincidentally
            // collide with name-table IDs. The schema TypeName is the
            // only reliable signal that a number *means* a reference
            // into a name table.
            if (localization is not null
                && (tag == "u32" || tag == "u16")
                && LocalizationProvider.CanResolveTypeName(row.TypeName)
                && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nameKey))
            {
                _resolvedName = localization.ResolveByFieldTypeName(row.TypeName, nameKey);
            }
        }
        else if (!row.Present
            && (row.MetaKind == 0 || row.MetaKind == 2)
            && ScalarFieldEditing.TryInferTypeTagFromSchema(row.TypeName, row.MetaSize, out var inferredTag)
            && ScalarFieldEditing.SupportedTypeTags.Contains(inferredTag))
        {
            // Absent scalar — schema knows the shape, just no value yet.
            // The edit panel opens empty; the user types a value, hits
            // Apply, and CommitFieldEdit routes the call through the
            // SetScalarFieldPresent path because Present is still false.
            _typeTag = inferredTag;
            _committedRawText = string.Empty;
            _rawText = string.Empty;
            IsEditable = true;
        }
        else
        {
            _typeTag = string.Empty;
            _committedRawText = string.Empty;
            _rawText = string.Empty;
            IsEditable = false;
        }
    }

    public DecodedFieldRow Row => _row;

    public int FieldIndex => _row.FieldIndex;
    public string Name => _row.Name;
    public string TypeName => _row.TypeName;
    public string Kind => _row.Kind;

    /// <summary>
    /// Latest-known presence flag for the field. Initialized from
    /// <c>row.Present</c> at construction; updated by
    /// <see cref="ApplyCommittedValue"/> after a successful mutation
    /// so an absent→present promotion flips the column live without
    /// rebuilding the row VM. The Present column in the fields
    /// DataGrid binds here.
    /// </summary>
    [ObservableProperty]
    private bool _present;

    /// <summary>
    /// Display string for the Value column. Always tracks the latest known
    /// formatted value — updated by <see cref="ApplyCommittedValue"/> after
    /// a successful mutation so the read row reflects the new bytes without
    /// rebuilding the wrapper.
    /// </summary>
    [ObservableProperty]
    private string _displayValue;

    /// <summary>
    /// Resolved item name for u32-shaped scalars whose value matches a
    /// known item key. Empty string when no match (or no localization
    /// available); the column binding hides empty cells naturally.
    /// Shape: "English" or "English / Local" depending on whether a
    /// secondary language is selected.
    /// </summary>
    [ObservableProperty]
    private string _resolvedName = string.Empty;

    /// <summary>True when this row is a top-level-block scalar of a supported type.</summary>
    public bool IsEditable { get; }

    /// <summary>True when nested data is reachable (proxied from the inner row).</summary>
    public bool HasNested => _row.HasNested;

    /// <summary>The lowercase type tag (u32, i64, f32, bool, …) or empty for non-scalars.</summary>
    public string TypeTag => _typeTag;

    /// <summary>
    /// Editable raw text — initially the number / boolean parsed out of the
    /// formatted display value, edited live by the editor TextBox, and
    /// snapped back to <see cref="_committedRawText"/> if the user cancels.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirtyEdit))]
    private string _rawText;

    /// <summary>Latest validation error from a failed commit; null when valid.</summary>
    [ObservableProperty]
    private string? _editError;

    /// <summary>True while the user has typed something that differs from the committed value.</summary>
    public bool IsDirtyEdit => IsEditable && !string.Equals(RawText, _committedRawText, StringComparison.Ordinal);

    /// <summary>
    /// Commit a fresh decoded view of this row (from a re-fetch after
    /// mutation). Updates DisplayValue and resets the edit baseline so the
    /// edit textbox no longer reads as "dirty".
    /// </summary>
    public void ApplyCommittedValue(DecodedFieldRow fresh)
    {
        DisplayValue = fresh.Value;
        Present = fresh.Present;
        if (IsEditable
            && ScalarFieldEditing.TryParse(fresh.Value, out var raw, out var tag)
            && tag == _typeTag)
        {
            _committedRawText = raw;
            RawText = raw;
            // Re-resolve the name column when the underlying raw value
            // changed. Without this, editing _itemKey leaves the Name
            // column showing the *previous* item's name until the user
            // navigates away and back. Same TypeName-gated path the
            // constructor uses, so the rules stay consistent (only
            // ItemKey / FactionKey / CharacterKey / GimmickInfoKey-
            // typed u32 fields get a name).
            if (_localization is not null
                && (tag == "u32" || tag == "u16")
                && LocalizationProvider.CanResolveTypeName(_row.TypeName)
                && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nameKey))
            {
                ResolvedName = _localization.ResolveByFieldTypeName(_row.TypeName, nameKey);
            }
        }
        EditError = null;
    }

    /// <summary>Revert the editor text to the last committed value.</summary>
    public void RevertEdit()
    {
        RawText = _committedRawText;
        EditError = null;
    }
}
