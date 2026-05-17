using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One flat record from <see cref="ISaveLoader.ListAllItems"/>.
/// <c>repr(C)</c> blittable struct with the exact 64-byte layout the
/// Rust C ABI's <c>CrimsonItemRecord</c> emits — see the upstream
/// doc in <c>vendor/crimson-rs/src/c_abi/all_items.rs</c>.
///
/// <para>
/// Single-call cross-container enumerator: covers
/// <see cref="ContainerKind.ActiveEquip"/> +
/// <see cref="ContainerKind.ActiveUseReserve"/> +
/// <see cref="ContainerKind.Inventory"/> +
/// <see cref="ContainerKind.MercenaryEquip"/> +
/// <see cref="ContainerKind.MercenaryInventory"/>. Replaces the
/// inventory-only <see cref="ISaveLoader.ListInventoryItems"/> when
/// callers need equipped gear / reserve slots / mercenary-side items
/// in the same enumeration.
/// </para>
///
/// <para>
/// The <see cref="PathStep0Field"/> / <see cref="PathStep0Element"/> /
/// <see cref="PathStep1Field"/> / <see cref="PathStep1Element"/> tuple
/// plugs straight into
/// <see cref="ISaveLoader.SetScalarField(int, System.ReadOnlySpan{PathStep}, int, System.ReadOnlySpan{byte})"/>
/// and friends — same path-step semantics as the rest of the C ABI.
/// Use <see cref="ToPathSteps"/> to materialize the 2-step descent
/// without boilerplate.
/// </para>
///
/// <para>
/// <b>Validity window</b>: positional fields stay valid only until
/// the next length-changing mutation in the relevant list. Pair
/// snapshots with <see cref="ISaveLoader.GetMutationVersion"/> for
/// O(1) staleness detection.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public readonly struct ItemRecord
{
    /// <summary>Top-level TOC block index (use as <c>blockIndex</c> arg to mutation ABIs).</summary>
    public readonly uint BlockIndex;

    /// <summary>One of <see cref="ContainerKind"/> constants.</summary>
    public readonly uint ContainerKindValue;

    /// <summary>
    /// Number of valid descent steps. Currently always 2 — future
    /// container kinds may set a different length; callers MUST honour it.
    /// </summary>
    public readonly uint PathLen;

    /// <summary>First descent step's <c>field_idx</c>.</summary>
    public readonly uint PathStep0Field;

    /// <summary>First descent step's <c>element_idx</c>.</summary>
    public readonly uint PathStep0Element;

    /// <summary>Second descent step's <c>field_idx</c>.</summary>
    public readonly uint PathStep1Field;

    /// <summary>
    /// Second descent step's <c>element_idx</c> (ignored for Locator
    /// descents — value 0).
    /// </summary>
    public readonly uint PathStep1Element;

    /// <summary>
    /// <c>_inventoryKey</c> for <see cref="ContainerKind.Inventory"/>
    /// entries (u16 widened); 0 for other kinds.
    /// </summary>
    public readonly uint InventoryKey;

    /// <summary><c>ItemKey</c> (gamedata template id) for this slot.</summary>
    public readonly uint ItemKey;

    /// <summary>
    /// <c>_slotNo</c> (inventory) or <c>_occupiedSlotNo</c> (equipment)
    /// widened to u32; 0 when absent.
    /// </summary>
    public readonly uint SlotNo;

    /// <summary>Bitfield — see <see cref="ItemRecordFlags"/>.</summary>
    public readonly uint Flags;

    /// <summary>
    /// For mercenary kinds: <c>_characterKey &amp; 0xFFFFFF</c> (cat-byte
    /// stripped). For active kinds: <c>MercenaryClanSaveData._lastFocusCharacterKey</c>.
    /// </summary>
    public readonly uint OwnerCharacterKey;

    /// <summary><c>_itemNo</c> — per-save unique instance id, stable across mutations until removed.</summary>
    public readonly ulong ItemNo;

    /// <summary>
    /// For mercenary kinds: enclosing
    /// <c>MercenarySaveData._mercenaryNo</c>. 0 for active kinds.
    /// </summary>
    public readonly ulong OwnerMercenaryNo;

    /// <summary>Typed accessor over <see cref="ContainerKindValue"/>.</summary>
    public ContainerKind Container => (ContainerKind)ContainerKindValue;

    public bool IsLocked => (Flags & ItemRecordFlags.Locked) != 0;
    public bool IsNewMark => (Flags & ItemRecordFlags.NewMark) != 0;
    public bool HasDyeData => (Flags & ItemRecordFlags.HasDyeData) != 0;
    public bool HasSocketData => (Flags & ItemRecordFlags.HasSocketData) != 0;
    public bool OwnerIsMainMercenary => (Flags & ItemRecordFlags.OwnerIsMainMercenary) != 0;
    public bool IsPlayerOwned => (Flags & ItemRecordFlags.IsPlayerOwned) != 0;

    /// <summary>
    /// Materialize the descent path as a stack-allocated
    /// <see cref="PathStep"/>[] suitable for any
    /// <c>SetScalarFieldPath</c>-shaped mutation. Honours
    /// <see cref="PathLen"/> so future longer paths still work without
    /// caller changes.
    /// </summary>
    public PathStep[] ToPathSteps()
    {
        var len = (int)PathLen;
        var path = new PathStep[len];
        if (len > 0)
        {
            path[0] = new PathStep(PathStep0Field, PathStep0Element);
        }
        if (len > 1)
        {
            path[1] = new PathStep(PathStep1Field, PathStep1Element);
        }
        return path;
    }
}

/// <summary>
/// Container classification for
/// <see cref="ItemRecord.ContainerKindValue"/>. Numeric values are
/// part of the C ABI surface — never reassign.
/// </summary>
public enum ContainerKind : uint
{
    /// <summary><c>EquipmentSaveData._list[N]._item&lt;locator&gt;</c> — active character's equipped gear.</summary>
    ActiveEquip = 0,

    /// <summary><c>EquipmentSaveData._useItemSaveList[N]._reserveItem&lt;locator&gt;</c> — active character's quick-use reserve.</summary>
    ActiveUseReserve = 1,

    /// <summary><c>InventorySaveData._inventoryList[N]._itemList[M]</c> — active character's inventory.</summary>
    Inventory = 2,

    /// <summary>
    /// <c>MercenaryClanSaveData._mercenaryDataList[N]._equipItemList[M]</c>
    /// — equipped gear on a mercenary / mount / inactive playable character.
    /// </summary>
    MercenaryEquip = 3,

    /// <summary>
    /// <c>MercenaryClanSaveData._mercenaryDataList[N]._inventoryItemList[M]</c>
    /// — items carried by a mercenary / mount.
    /// </summary>
    MercenaryInventory = 4,
}

/// <summary>
/// Bit constants for <see cref="ItemRecord.Flags"/>. Mirrors the
/// Rust-side <c>item_record_flags</c> module.
/// </summary>
public static class ItemRecordFlags
{
    /// <summary><c>_isLocked</c> on the item was present and <c>true</c>.</summary>
    public const uint Locked = 1u << 0;

    /// <summary><c>_isNewMark</c> on the item was present and <c>true</c>.</summary>
    public const uint NewMark = 1u << 1;

    /// <summary><c>_itemDyeDataList</c> is present and has <c>count &gt; 0</c>.</summary>
    public const uint HasDyeData = 1u << 2;

    /// <summary><c>_socketSaveDataList</c> is present and has <c>count &gt; 0</c>.</summary>
    public const uint HasSocketData = 1u << 3;

    /// <summary>
    /// Enclosing <c>MercenarySaveData._isMainMercenary</c> was
    /// <c>true</c>. Always 0 for non-mercenary container kinds.
    /// </summary>
    public const uint OwnerIsMainMercenary = 1u << 4;

    /// <summary>
    /// Item belongs to one of the three playable characters or to a
    /// mount they own. Filter set when:
    /// <list type="bullet">
    /// <item>container_kind is active equip / reserve / inventory; OR</item>
    /// <item>container_kind is mercenary AND the enclosing mercenary's
    ///   <c>_characterKey &amp; 0xFFFFFF</c> is in <c>{1, 4, 6}</c>
    ///   (Kliff / Damine / Oongka); OR</item>
    /// <item>container_kind is mercenary AND the enclosing mercenary's
    ///   <c>_ownedCharacterKey &amp; 0xFFFFFF</c> is in <c>{1, 4, 6}</c>
    ///   (mount owned by a playable).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The strict flag misses 8 player-controlled records on the
    /// slot103 baseline (Tiuta_kliff horse + Stefano animal — both
    /// have <c>_ownedCharacterKey</c> absent). The C# editor widens
    /// these via a name-prefix check on the resolved owner template
    /// name — see
    /// <c>CrimsonAtomtic.Ui.Services.LocalizationProvider.IsPlayerEditableItem</c>
    /// and the recipe in
    /// <c>vendor/crimson-rs/docs/dye-editor-scope.md</c>
    /// §"C# editor — IS_PLAYER_OWNED widening recipe".
    /// </remarks>
    public const uint IsPlayerOwned = 1u << 5;
}
