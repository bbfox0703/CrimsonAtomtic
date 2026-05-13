using System.Globalization;
using CrimsonAtomtic.SaveModel;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Wrapper for one element shown in the drill-into-list view (when
/// the user lands on an <c>ObjectList</c> field's element picker —
/// e.g. <c>itemList[14]</c>). Adds three computed columns on top of the
/// underlying <see cref="BlockDetails"/>:
/// <list type="bullet">
///   <item><see cref="KeyText"/> — the raw u32 value of any named-key
///   scalar field on this element (ItemKey, FactionKey, or
///   CharacterKey). Empty when the element doesn't carry one.</item>
///   <item><see cref="ResolvedName"/> — the localized name resolved
///   through <see cref="LocalizationProvider.ResolveByFieldTypeName"/>.</item>
///   <item><see cref="NestedMatchHaystack"/> — a flattened lower-case
///   string of every resolved name found in nested <c>ObjectList</c>
///   children, so the element picker filter can find e.g. "Gold"
///   inside any bag without the user having to drill into each one.</item>
/// </list>
///
/// Without these, the list view shows 14 identical <c>ItemSaveData</c>
/// rows that are visually indistinguishable. With them, the user can
/// tell at a glance which slot holds which item — and from the
/// <c>InventorySaveData._inventorylist</c> level, "Gold" filters down
/// to just the bag(s) holding gold.
/// </summary>
public sealed class ElementRowViewModel
{
    public BlockDetails Block { get; }
    public string KeyText { get; }
    public string ResolvedName { get; }

    /// <summary>
    /// Numeric form of the row's ItemKey, used to drive the icon
    /// column. <c>0</c> when this row isn't an item (or its key is
    /// a non-ItemKey like FactionKey / CharacterKey / InventoryKey —
    /// those don't have icons in the reference pack). The icon
    /// converter treats 0 as "no icon".
    /// </summary>
    public uint IconItemKey { get; }

    /// <summary>
    /// Concatenated, lower-cased haystack of every resolved name found
    /// inside this element's nested ObjectList children. Empty when the
    /// element has no name-bearing children, or when none of them
    /// resolves. Used by the elements filter so "Gold" matches a bag
    /// element that *contains* Gold without the user having to open
    /// the bag first.
    /// </summary>
    public string NestedMatchHaystack { get; }

    public ElementRowViewModel(BlockDetails block, LocalizationProvider? localization)
    {
        Block = block;
        // Locate the directly-bearing key field. Two depths covered:
        //   1. Directly on this element's Fields (e.g. ItemSaveData._itemKey).
        //   2. One level down through any inline locator child
        //      (e.g. EquipSlotElementSaveData._item → ItemSaveData._itemKey).
        // Without (2) the EquipmentSaveData → list[18] view shows blank
        // Key/Name columns even though every slot wraps a real item one
        // level deeper. Anything that doesn't yield a known key at
        // either depth falls through to empty cells.
        var keyField = FindKeyField(block)
                       ?? FindKeyFieldInChildren(block);

        if (keyField is not null
            && ScalarFieldEditing.TryParse(keyField.Value, out var raw, out var tag)
            && (tag == "u32" || tag == "u16")
            && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
        {
            KeyText = raw;
            ResolvedName = localization is null
                ? string.Empty
                : localization.ResolveByFieldTypeName(keyField.TypeName, key);
            // Only ItemKey-typed rows feed the icon column — the
            // reference pack is keyed by ItemKey, so a CharacterKey or
            // FactionKey value here would just produce a miss. Leave
            // IconItemKey = 0 for non-ItemKey rows so the converter
            // returns null and the cell stays empty.
            IconItemKey = keyField.TypeName == "ItemKey" ? key : 0u;
        }
        else
        {
            KeyText = string.Empty;
            ResolvedName = string.Empty;
            IconItemKey = 0u;
        }

        NestedMatchHaystack = localization is null
            ? string.Empty
            : BuildNestedHaystack(block, localization);

        // Fill-stack affordance comes in two flavours:
        //
        //   1. Single-item: this row IS an ItemSaveData-shaped element
        //      (carries ItemKey + _stackCount on its own fields).
        //      Button fills just this row's _stackCount.
        //   2. Container: this row has a nested ObjectList of
        //      single-item-shaped sub-elements (e.g.
        //      InventoryElementSaveData → _itemList[N]). Button fills
        //      every sub-element.
        //
        // We deliberately exclude the container case when the row
        // itself resolves to a *named entity* (CharacterKey / FactionKey
        // / etc.) — that's how MercenarySaveData earned a "Fill stacks"
        // button it had no business carrying. A mercenary is a person
        // who happens to own items; bulk-filling their gear isn't
        // semantically a "container fill". The InventoryKey case
        // (Backpack / Camp & Contributions / …) is the legitimate
        // container shape; we gate on that explicitly.
        IsSingleFillCandidate = HasOwnStackableScalars(block);
        IsContainerFillCandidate =
            !IsSingleFillCandidate
            && HasStackableInventoryList(block)
            && IsContainerRowByKeyType(keyField);
    }

    /// <summary>
    /// True when this element block has at least one ObjectList field
    /// whose elements carry both an ItemKey scalar AND a
    /// <c>_stackCount</c> field — the universal "this looks like an
    /// inventory container holding stackable items" signal. We don't
    /// hardcode <c>ClassName == "InventoryElementSaveData"</c>
    /// because other future schemas might reuse the same shape;
    /// shape-checking keeps the gate honest.
    /// </summary>
    private static bool HasStackableInventoryList(BlockDetails block)
    {
        foreach (var field in block.Fields)
        {
            if (field.Elements is not { Count: > 0 } items)
            {
                continue;
            }
            foreach (var item in items)
            {
                if (HasOwnStackableScalars(item))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="block"/> itself directly carries both
    /// an <c>ItemKey</c> scalar AND a <c>_stackCount</c> scalar — i.e.
    /// the element IS a stackable item (an ItemSaveData). Used both
    /// to detect single-item fill candidates and as the inner gate of
    /// <see cref="HasStackableInventoryList"/>.
    /// </summary>
    private static bool HasOwnStackableScalars(BlockDetails block)
    {
        var hasItemKey = false;
        var hasStackCount = false;
        foreach (var field in block.Fields)
        {
            if (field.TypeName == "ItemKey"
                && (field.Kind == "fixed_prefix" || field.Kind == "fixed_suffix"))
            {
                hasItemKey = true;
            }
            else if (field.Name == "_stackCount"
                     && (field.Kind == "fixed_prefix" || field.Kind == "fixed_suffix"))
            {
                hasStackCount = true;
            }
            if (hasItemKey && hasStackCount)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Decide whether the row's own key field (if any) means
    /// "container" vs "named entity". Containers (InventoryKey) are
    /// allowed to get the bulk-fill button; named entities
    /// (CharacterKey / FactionKey / ItemKey / GimmickInfoKey / …) are
    /// not — they're individuals that happen to own items, not
    /// containers conceptually.
    /// </summary>
    private static bool IsContainerRowByKeyType(DecodedFieldRow? keyField)
    {
        if (keyField is null)
        {
            // No key field at all — generic struct, treat as container.
            return true;
        }
        return keyField.TypeName == "InventoryKey";
    }

    /// <summary>
    /// True iff this row is itself an ItemSaveData-shaped element —
    /// carries both <c>ItemKey</c> and <c>_stackCount</c> on its own
    /// fields. The per-row "Fill stack" button uses this to enable
    /// single-item fill (set this one row's _stackCount to its
    /// iteminfo max). Filtered out of the container case so we don't
    /// show two buttons for the same row.
    /// </summary>
    public bool IsSingleFillCandidate { get; }

    /// <summary>
    /// True iff this row is a *container* of stackable items: has at
    /// least one nested ObjectList whose elements look like
    /// ItemSaveData, AND the row's own key (if any) is a container
    /// key (<c>InventoryKey</c>) rather than a named entity
    /// (<c>CharacterKey</c> / <c>FactionKey</c> / …). The legacy
    /// "Fill stacks" alias on container rows still routes through
    /// this flag.
    /// </summary>
    public bool IsContainerFillCandidate { get; }

    /// <summary>Either single or container — drives the "Fill stack(s)" button visibility.</summary>
    public bool IsBulkFillCandidate => IsSingleFillCandidate || IsContainerFillCandidate;

    /// <summary>Label for the fill button — switches between "Fill stack" (1) and "Fill stacks" (N).</summary>
    public string FillButtonLabel => IsSingleFillCandidate ? "Fill stack" : "Fill stacks";

    /// <summary>
    /// Find the first scalar field on <paramref name="block"/> whose
    /// schema TypeName indicates a known name namespace. Returns
    /// <c>null</c> when no such field exists on this block.
    /// </summary>
    private static DecodedFieldRow? FindKeyField(BlockDetails block)
    {
        foreach (var field in block.Fields)
        {
            if (IsNameKey(field.TypeName)
                && (field.Kind == "fixed_prefix" || field.Kind == "fixed_suffix"))
            {
                return field;
            }
        }
        return null;
    }

    private static DecodedFieldRow? FindKeyFieldInChildren(BlockDetails block)
    {
        foreach (var field in block.Fields)
        {
            if (field.Child is { } child)
            {
                var nested = FindKeyField(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Schema TypeNames that <see cref="LocalizationProvider.ResolveByFieldTypeName"/>
    /// knows how to resolve. Kept in sync with that map by convention —
    /// adding a row there means adding the same TypeName here so the
    /// list-picker views pick it up too.
    /// </summary>
    private static bool IsNameKey(string typeName) =>
        typeName is "ItemKey"
                 or "FactionKey"
                 or "CharacterKey"
                 or "GimmickInfoKey"
                 or "LevelGimmickSceneObjectInfoKey"
                 or "InventoryKey";

    /// <summary>
    /// Walk every nested <c>ObjectList</c> field of this element one
    /// level deep, resolve each sub-element's name, and join the
    /// matches into a single lower-cased substring haystack.
    /// One level is enough for the common case (InventoryElementSaveData
    /// → _itemList[]); deeper nesting (lists-of-lists) doesn't appear
    /// in any save shape we've inspected against 1.06.
    /// </summary>
    private static string BuildNestedHaystack(BlockDetails block, LocalizationProvider localization)
    {
        // Hot-path early exit: most blocks don't have child lists.
        var found = false;
        foreach (var f in block.Fields)
        {
            if (f.Elements is { Count: > 0 })
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var field in block.Fields)
        {
            if (field.Elements is not { Count: > 0 } elements)
            {
                continue;
            }
            foreach (var child in elements)
            {
                var childKeyField = FindKeyField(child) ?? FindKeyFieldInChildren(child);
                if (childKeyField is null
                    || !ScalarFieldEditing.TryParse(childKeyField.Value, out var raw, out var tag)
                    || (tag != "u32" && tag != "u16")
                    || !uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var childKey))
                {
                    continue;
                }
                var name = localization.ResolveByFieldTypeName(childKeyField.TypeName, childKey);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append('\n'); // splits siblings — "\n" is never part of a resolved name
                }
                sb.Append(name.ToLowerInvariant());
            }
        }
        return sb.ToString();
    }

    // Proxy properties mirroring the existing DataGrid bindings that
    // worked directly off BlockDetails. Keeping the surface flat means
    // the XAML stays a property-read per column with no nested paths.
    public int ClassIndex => Block.ClassIndex;
    public string ClassName => Block.ClassName;
    public long DataOffset => Block.DataOffset;
    public long DataSize => Block.DataSize;
}
