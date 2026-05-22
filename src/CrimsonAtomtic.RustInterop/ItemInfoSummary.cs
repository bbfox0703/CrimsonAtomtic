using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Static-metadata booleans for an iteminfo row. Mirrors the
/// <c>CRIMSON_ITEMINFO_FLAG_*</c> constants exposed by the crimson-rs
/// C ABI (see <c>vendor/crimson-rs/src/c_abi/iteminfo.rs</c>); bit
/// positions are part of the ABI contract and must not be reordered.
/// </summary>
/// <remarks>
/// <para>
/// The underlying ABI bitmask is <c>u32</c> with 28 bits in use
/// (bits 0–27). Bits 28–31 are reserved for future flags upstream.
/// </para>
/// <para>
/// Most flags answer "is this item …?" predicates; a few
/// (<see cref="HideFromInventoryOnPopItem"/>,
/// <see cref="EnableAlertSystemToUi"/>,
/// <see cref="DeleteByGimmickUnlock"/>, …) describe behaviour rather
/// than identity. Flag display labels + tooltip explanations live in
/// the localization dictionaries under <c>ItemFlagLabel*</c> /
/// <c>ItemFlagTip*</c>.
/// </para>
/// </remarks>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'Flags' suffix is the idiomatic name for a [Flags] enum here; renaming would be misleading.")]
public enum ItemInfoFlags : uint
{
    /// <summary>No flags set.</summary>
    None                          = 0,

    /// <summary>Item is blocked from gameplay (developer / unused entry).</summary>
    IsBlocked                     = 1u << 0,

    /// <summary>Has at least one dye slot.</summary>
    IsDyeable                     = 1u << 1,

    /// <summary>Removed from inventory when its endurance hits 0.</summary>
    IsDestroyWhenBroken           = 1u << 2,

    /// <summary>Only meaningful inside player housing — won't appear in regular inventory UIs.</summary>
    IsHousingOnly                 = 1u << 3,

    /// <summary>Eligible for the equip quick-slot bar (1.08-introduced UI affordance).</summary>
    IsEquipQuickSlotVisible       = 1u << 4,

    /// <summary>Flagged as a story / quest important item.</summary>
    IsImportantItem               = 1u << 5,

    /// <summary>Equippable in the off-hand shield slot.</summary>
    IsShieldItem                  = 1u << 6,

    /// <summary>Tower-shield variant (larger hitbox / different stance).</summary>
    IsTowerShieldItem             = 1u << 7,

    /// <summary>"Wild" item — gathered from the world rather than crafted.</summary>
    IsWild                        = 1u << 8,

    /// <summary>Inventory-hides the item after the PopItem use action runs.</summary>
    HideFromInventoryOnPopItem    = 1u << 9,

    /// <summary>Player is allowed to discard.</summary>
    Discardable                   = 1u << 10,

    /// <summary>Eligible for the trade-market posting flow.</summary>
    IsRegisterTradeMarket         = 1u << 11,

    /// <summary>Reserved for in-engine editor tooling.</summary>
    IsEditorUsable                = 1u << 12,

    /// <summary>Has an editable "grime" / dirt overlay.</summary>
    IsEditableGrime               = 1u << 13,

    /// <summary>Triggers its use effect on pickup rather than from inventory.</summary>
    UseImmediately                = 1u << 14,

    /// <summary>Caps the runtime stack count to <c>max_stack_count</c> (rather than the inventory-slot default).</summary>
    ApplyMaxStackCap              = 1u << 15,

    /// <summary>Blocked from store sell-back even if the store category would normally accept it.</summary>
    IsBlockedStoreSell            = 1u << 16,

    /// <summary>Granted via a pre-order entitlement bundle.</summary>
    IsPreorderItem                = 1u << 17,

    /// <summary>Carries an inventory-time buff payload (e.g. permanent stat boost while held).</summary>
    IsHasItemUseDataInventoryBuff = 1u << 18,

    /// <summary>Preserved during extraction-style runs (rogue-lite carry-out mechanic).</summary>
    IsPreservedOnExtract          = 1u << 19,

    /// <summary>Item-pickup raises a UI alert.</summary>
    EnableAlertSystemToUi         = 1u << 20,

    /// <summary>Use action triggers a save-game write.</summary>
    IsSaveGameDataAtUseItem       = 1u << 21,

    /// <summary>Use action ends the session (logout-style).</summary>
    IsLogoutAtUseItem             = 1u << 22,

    /// <summary>Equippable on a player's clone-actor (multiplayer / mirror combat).</summary>
    EnableEquipInCloneActor       = 1u << 23,

    /// <summary>Eligible for the disassemble / salvage workflow.</summary>
    CanDisassemble                = 1u << 24,

    /// <summary>Sealable against all gimmick effects (no environmental interactions strip it).</summary>
    IsAllGimmickSealable          = 1u << 25,

    /// <summary>Auto-deleted when its bound gimmick gets unlocked.</summary>
    DeleteByGimmickUnlock         = 1u << 26,

    /// <summary>Used as a drop-set targeting key (loot-table marker).</summary>
    UseDropSetTarget              = 1u << 27,
}

/// <summary>
/// One-shot static-metadata snapshot for a single iteminfo row.
/// Mirrors <c>CrimsonItemInfoSummary</c> in
/// <c>vendor/crimson-rs/src/c_abi/iteminfo.rs</c> byte-for-byte.
/// </summary>
/// <remarks>
/// <para>
/// Layout is pinned in the Rust source via a <c>const _: () = { assert!(size_of == 80) }</c>
/// check; the C# side asserts the same in
/// <c>NativeItemInfoSummaryTests.Layout_MatchesRustAbi</c>. Field order
/// is: <c>u64</c> × 3 → <c>u32</c> × 9 → <c>u16</c> × 4 → <c>u8</c> × 8,
/// with the trailing <c>_Reserved</c> byte at offset 75 and four bytes
/// of struct-alignment padding to round the total to 80 bytes.
/// </para>
/// <para>
/// Variable-length data (string_key, full icon path, the per-item
/// allowed-gem list, …) is intentionally NOT included — callers reach
/// for the dedicated lookups (<c>LookupStringKey</c>,
/// <c>LookupSocketCaps</c>, …) when they need those.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ItemInfoSummary
{
    /// <summary>Maximum stack count for the item.</summary>
    public readonly ulong MaxStackCount;

    /// <summary>Cooldown between uses, in game-defined ticks. Sign-typed; -1 = no cooldown.</summary>
    public readonly long Cooltime;

    /// <summary>Respawn timer for world-placed instances, in seconds.</summary>
    public readonly long RespawnTimeSeconds;

    /// <summary>Item key (echoed for convenience — same as the lookup argument).</summary>
    public readonly uint Key;

    /// <summary>Static-metadata bitmask. See <see cref="ItemInfoFlags"/>.</summary>
    public readonly ItemInfoFlags Flags;

    /// <summary>Hash of the primary icon's <c>StringInfoKey</c>. 0 when the item has no icon list entry.</summary>
    public readonly uint IconPathHash;

    /// <summary><c>EquipTypeInfoKey</c> — which equipment slot family the item belongs to.</summary>
    public readonly uint EquipTypeInfo;

    /// <summary><c>EquipableHash</c> — cross-references the engine's equipable lookup.</summary>
    public readonly uint EquipableHash;

    /// <summary>Minimum character level required to equip.</summary>
    public readonly uint EquipableLevel;

    /// <summary><c>KnowledgeInfoKey</c> — the knowledge entry unlocked by interacting with this item, if any.</summary>
    public readonly uint KnowledgeInfo;

    /// <summary>Material item key cross-reference (e.g. dye base material).</summary>
    public readonly uint MaterialKey;

    /// <summary><c>GimmickInfoKey</c> — the world-gimmick interaction the item is bound to, if any.</summary>
    public readonly uint GimmickInfo;

    /// <summary><c>CategoryKey</c> — broad item category (weapon / armour / consumable / …).</summary>
    public readonly ushort CategoryInfo;

    /// <summary><c>InventoryKey</c> — default destination bag when the item is granted.</summary>
    public readonly ushort InventoryInfo;

    /// <summary>Minimum enchant level required for extract / disassemble flows.</summary>
    public readonly ushort MinimumExtractEnchantLevel;

    /// <summary>Maximum endurance for the item (durability cap, in engine units).</summary>
    public readonly ushort MaxEndurance;

    /// <summary>Raw item type byte (engine taxonomy — 0 = arrow, 24 = plate-armor helm, etc.).</summary>
    public readonly byte ItemType;

    /// <summary>Tier / rarity step (engine-defined gradient).</summary>
    public readonly byte ItemTier;

    /// <summary>Quick-slot index when the item is pinned to the action bar.</summary>
    public readonly byte QuickSlotIndex;

    /// <summary>Charge-type for chargeable consumables.</summary>
    public readonly byte ItemChargeType;

    /// <summary>Alert variant emitted when the item triggers <see cref="ItemInfoFlags.EnableAlertSystemToUi"/>.</summary>
    public readonly byte UsableAlertType;

    /// <summary>Knowledge-obtain method (instant vs trigger-based).</summary>
    public readonly byte KnowledgeObtainType;

    /// <summary>Apply-drop-stat type (loot-table grouping byte).</summary>
    public readonly byte ApplyDropStatType;

    /// <summary>Reserved padding byte (always 0 — explicit so the struct round-trips byte-for-byte against the Rust ABI).</summary>
    public readonly byte ReservedByte;
}
