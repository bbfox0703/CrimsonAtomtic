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
        EnclosingPath = enclosingPath;

        // For scalar rows, split the pre-formatted value ("123 <u32>") into
        // raw + tag once so the editor can show the bare number. Anything
        // that doesn't parse (locator strings, "(absent)", etc.) leaves
        // both fields empty and IsEditable = false.
        if (ScalarFieldEditing.IsScalarKind(row.Kind)
            && ScalarFieldEditing.TryParse(row.Value, out var raw, out var tag)
            && ScalarFieldEditing.SupportedTypeTags.Contains(tag))
        {
            _typeTag = tag;
            _committedRawText = raw;
            _rawText = raw;
            IsEditable = true;

            // Item-name resolution. Restricted to ItemKey-typed u32
            // fields to avoid false positives — every save has dozens
            // of u32 scalars (slot counts, inventory keys, etc.) that
            // would coincidentally collide with item-table IDs and
            // surface confusing "name" strings. ItemKey is the schema
            // type used everywhere a real item ID appears, so this
            // filter cleanly separates "actual item reference" from
            // "happens to be a small integer".
            if (localization is not null
                && tag == "u32"
                && row.TypeName == "ItemKey"
                && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
            {
                var (english, secondary) = localization.ResolveItemName(itemId);
                _resolvedName = FormatResolvedName(english, secondary);
            }
        }
        else
        {
            _typeTag = string.Empty;
            _committedRawText = string.Empty;
            _rawText = string.Empty;
            IsEditable = false;
        }
    }

    private static string FormatResolvedName(string? english, string? secondary)
    {
        var hasEnglish = !string.IsNullOrEmpty(english);
        var hasSecondary = !string.IsNullOrEmpty(secondary);
        if (!hasEnglish && !hasSecondary) return string.Empty;
        if (!hasSecondary) return english!;
        if (!hasEnglish) return secondary!;
        return $"{english} / {secondary}";
    }

    public DecodedFieldRow Row => _row;

    public int FieldIndex => _row.FieldIndex;
    public string Name => _row.Name;
    public string TypeName => _row.TypeName;
    public string Kind => _row.Kind;
    public bool Present => _row.Present;

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
        if (IsEditable
            && ScalarFieldEditing.TryParse(fresh.Value, out var raw, out var tag)
            && tag == _typeTag)
        {
            _committedRawText = raw;
            RawText = raw;
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
