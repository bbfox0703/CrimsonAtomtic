using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One flat record from <see cref="ISaveLoader.ListInventoryItems"/>.
/// <c>repr(C)</c> blittable struct with the exact 48-byte layout the
/// Rust C ABI's <c>CrimsonInventoryItemRecord</c> emits — see the
/// upstream doc in <c>vendor/crimson-rs/src/c_abi/mod.rs</c>.
///
/// <para>
/// <see cref="InventoryElementIndex"/> + <see cref="ItemElementIndex"/>
/// form the descent path the C ABI uses to address this exact slot
/// from <c>SetScalarField</c> + friends:
/// <c>path = [(field=0 (_inventorylist), element=InventoryElementIndex),
/// (field=2 (_itemList), element=ItemElementIndex)]</c>.
/// </para>
///
/// <para>
/// <b>Validity window</b>: positional fields stay valid only until the
/// next length-changing mutation in the relevant inventory list. Pair
/// snapshots with <see cref="ISaveLoader.GetMutationVersion"/> for
/// O(1) staleness detection.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public readonly struct InventoryItemRecord
{
    /// <summary>Top-level <c>InventorySaveData</c> block index in the save.</summary>
    public readonly uint BlockIndex;

    /// <summary>Position in <c>_inventorylist[N]</c> (the container index, 0..17 in 1.07).</summary>
    public readonly uint InventoryElementIndex;

    /// <summary>Position in <c>_itemList[M]</c> (the item index within the container).</summary>
    public readonly uint ItemElementIndex;

    /// <summary>
    /// <c>InventoryKey</c> value from the container — the category id
    /// the <c>LocalizationProvider.InventoryContainerLabels</c> table
    /// labels (e.g. 2 = Backpack, 5 = Quest Artifacts). u16 widened to
    /// u32 for alignment.
    /// </summary>
    public readonly uint InventoryKey;

    /// <summary><c>ItemKey</c> for this item slot — the gamedata key consumers search by.</summary>
    public readonly uint ItemKey;

    /// <summary>
    /// <c>_transferredItemKey</c> — origin item key when this slot was
    /// transferred from another, encoded as
    /// <c>((srcKey &amp; 0xFFFF) &lt;&lt; 16) | 0x0101</c>. 0 when absent.
    /// </summary>
    public readonly uint TransferredItemKey;

    /// <summary><c>_slotNo</c> — visual slot within the container. u16 widened.</summary>
    public readonly uint SlotNo;

    /// <summary>
    /// Bitfield — see <see cref="InventoryItemFlags"/> constants. Bit 0
    /// = <c>_isLocked</c>, bit 1 = <c>_isNewMark</c>. Other bits reserved 0.
    /// </summary>
    public readonly uint Flags;

    /// <summary>
    /// <c>_itemNo</c> — per-save unique instance id, stable across
    /// mutations until the item is removed.
    /// </summary>
    public readonly ulong ItemNo;

    /// <summary><c>_stackCount</c> — current stack size.</summary>
    public readonly ulong StackCount;

    public bool IsLocked => (Flags & InventoryItemFlags.Locked) != 0;
    public bool IsNewMark => (Flags & InventoryItemFlags.NewMark) != 0;
}

/// <summary>
/// Bit constants for <see cref="InventoryItemRecord.Flags"/>. Mirrors
/// the Rust-side <c>inventory_item_flags</c> module.
/// </summary>
public static class InventoryItemFlags
{
    /// <summary><c>_isLocked</c> field was present and <c>true</c>.</summary>
    public const uint Locked = 1u << 0;

    /// <summary><c>_isNewMark</c> field was present and <c>true</c>.</summary>
    public const uint NewMark = 1u << 1;
}
