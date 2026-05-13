using System.Globalization;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Wrapper for one element shown in the drill-into-list view (when
/// the user lands on an <c>ObjectList</c> field's element picker —
/// e.g. <c>itemList[14]</c>). Adds two computed columns on top of the
/// underlying <see cref="BlockDetails"/>:
/// <list type="bullet">
///   <item><see cref="ItemKeyText"/> — the raw u32 value of any
///   <c>ItemKey</c>-typed scalar field on this element. Empty when
///   the element doesn't carry one.</item>
///   <item><see cref="ResolvedName"/> — the localized item name
///   resolved through <see cref="LocalizationProvider.ResolveItemNameFormatted"/>.</item>
/// </list>
///
/// Without these, the list view shows 14 identical <c>ItemSaveData</c>
/// rows that are visually indistinguishable. With them, the user can
/// tell at a glance which slot holds which item.
/// </summary>
public sealed class ElementRowViewModel
{
    public BlockDetails Block { get; }
    public string ItemKeyText { get; }
    public string ResolvedName { get; }

    public ElementRowViewModel(BlockDetails block, LocalizationProvider? localization)
    {
        Block = block;
        // Locate the ItemKey scalar. Two depths covered:
        //   1. Directly on this element's Fields (e.g. ItemSaveData._itemKey).
        //   2. One level down through any inline locator child
        //      (e.g. EquipSlotElementSaveData._item → ItemSaveData._itemKey).
        // Without (2) the EquipmentSaveData → list[18] view shows blank
        // ItemKey/Item-name columns even though every slot wraps a real
        // item one level deeper. Anything that doesn't yield an ItemKey
        // at either depth falls through to empty cells.
        var keyField = FindItemKeyField(block)
                       ?? FindItemKeyInChildren(block);

        if (keyField is not null
            && ScalarFieldEditing.TryParse(keyField.Value, out var raw, out var tag)
            && tag == "u32"
            && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemKey))
        {
            ItemKeyText = raw;
            ResolvedName = localization is null
                ? string.Empty
                : localization.ResolveItemNameFormatted(itemKey);
        }
        else
        {
            ItemKeyText = string.Empty;
            ResolvedName = string.Empty;
        }
    }

    private static DecodedFieldRow? FindItemKeyField(BlockDetails block)
    {
        foreach (var field in block.Fields)
        {
            if (field.TypeName == "ItemKey"
                && (field.Kind == "fixed_prefix" || field.Kind == "fixed_suffix"))
            {
                return field;
            }
        }
        return null;
    }

    private static DecodedFieldRow? FindItemKeyInChildren(BlockDetails block)
    {
        foreach (var field in block.Fields)
        {
            if (field.Child is { } child)
            {
                var nested = FindItemKeyField(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }

    // Proxy properties mirroring the existing DataGrid bindings that
    // worked directly off BlockDetails. Keeping the surface flat means
    // the XAML stays a property-read per column with no nested paths.
    public int ClassIndex => Block.ClassIndex;
    public string ClassName => Block.ClassName;
    public long DataOffset => Block.DataOffset;
    public long DataSize => Block.DataSize;
}
