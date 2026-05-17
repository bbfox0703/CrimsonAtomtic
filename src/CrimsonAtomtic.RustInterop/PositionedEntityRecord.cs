using System.Runtime.InteropServices;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One flat record from <see cref="ISaveLoader.ListFieldPositions"/>.
/// <c>repr(C)</c> blittable struct with the exact 56-byte layout the
/// Rust C ABI's <c>CrimsonPositionedEntityRecord</c> emits — see the
/// upstream doc in <c>vendor/crimson-rs/src/c_abi/positions.rs</c>.
///
/// <para>
/// Single-call cross-container enumerator covering the active
/// playable character (<see cref="PositionKind.ActiveChar"/>),
/// mercenaries / mounts (<see cref="PositionKind.Mercenary"/>), and
/// field gimmicks with a present transform
/// (<see cref="PositionKind.Gimmick"/>). Slot103 baseline yields
/// 3,317 records (1 + 76 + 3,240).
/// </para>
///
/// <para>
/// <see cref="PosX"/> / <see cref="PosY"/> / <see cref="PosZ"/> are
/// already in the global coordinate frame — same frame as the
/// in-game teleport markers. Apply the basemap affine to convert to
/// pixels:
/// <code>
/// px =  0.432044f * PosX + 5937.50f;
/// py = -0.433071f * PosZ + 1864.08f;
/// </code>
/// PosY is height and can be ignored for top-down plotting.
/// </para>
///
/// <para>
/// <b>Validity window</b>: positional fields (<see cref="BlockIndex"/>,
/// <see cref="ElementIndex"/>) stay valid only until the next
/// length-changing mutation in the source list. Pair snapshots with
/// <see cref="ISaveLoader.GetMutationVersion"/> for O(1) staleness
/// detection. Position scalars (<see cref="PosX"/> / <see cref="PosY"/>
/// / <see cref="PosZ"/> / <see cref="Yaw"/>) go stale on any mutation
/// to the source position field.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 56)]
public readonly struct PositionedEntityRecord
{
    /// <summary>Top-level TOC block index.</summary>
    public readonly uint BlockIndex;

    /// <summary>One of <see cref="PositionKind"/> constants.</summary>
    public readonly uint KindValue;

    /// <summary>Bitfield — see <see cref="PositionEntityFlags"/>.</summary>
    public readonly uint Flags;

    /// <summary>
    /// <c>_spawnFieldInfoKey</c> (mercenary) / <c>_fieldInfoKey</c>
    /// (active char) / enclosing <c>FieldSaveData._fieldInfoKey</c>
    /// (gimmick). Useful for filtering markers by region.
    /// </summary>
    public readonly uint FieldInfoKey;

    /// <summary>
    /// For <see cref="PositionKind.Mercenary"/>:
    /// <c>_characterKey &amp; 0xFFFFFF</c>.
    /// For <see cref="PositionKind.ActiveChar"/>:
    /// <c>MercenaryClanSaveData._lastFocusCharacterKey &amp; 0xFFFFFF</c>.
    /// 0 for <see cref="PositionKind.Gimmick"/>.
    /// </summary>
    public readonly uint CharacterKey;

    /// <summary>
    /// For <see cref="PositionKind.Gimmick"/>: <c>_gimmickInfoKey</c>
    /// (resolvable via <c>NativeKeyInfoCatalogs</c>'s gimmickinfo bridge).
    /// 0 otherwise.
    /// </summary>
    public readonly uint GimmickInfoKey;

    /// <summary>
    /// For <see cref="PositionKind.Gimmick"/>: <c>_fieldGimmickSaveDataKey</c>
    /// — the per-save slot id (distinct from <see cref="GimmickInfoKey"/>
    /// which is the gamedata template id). 0 otherwise.
    /// </summary>
    public readonly uint GimmickSaveDataKey;

    /// <summary>
    /// Within-list index. For <see cref="PositionKind.Mercenary"/>:
    /// the mercenary slot N inside <c>_mercenaryDataList</c>. For
    /// <see cref="PositionKind.ActiveChar"/>: the index into
    /// <c>_fieldSaveDataList</c>. For <see cref="PositionKind.Gimmick"/>:
    /// 0 (top-level block — use <see cref="BlockIndex"/>).
    /// </summary>
    public readonly uint ElementIndex;

    /// <summary>World X (east-west). Global frame.</summary>
    public readonly float PosX;

    /// <summary>World Y (height). Ignore for top-down plotting.</summary>
    public readonly float PosY;

    /// <summary>World Z (north-south). Global frame.</summary>
    public readonly float PosZ;

    /// <summary>
    /// Rotation around Y axis in radians. Mercenaries: read directly
    /// from <c>_spawnYaw</c>. Active char + gimmicks: derived from
    /// the quaternion via <c>2 * atan2(qy, qw)</c> for Y-up rotation.
    /// </summary>
    public readonly float Yaw;

    /// <summary>
    /// For <see cref="PositionKind.Mercenary"/>: <c>_mercenaryNo</c>
    /// (per-save unique instance id). 0 otherwise.
    /// </summary>
    public readonly ulong MercenaryNo;

    /// <summary>Typed accessor over <see cref="KindValue"/>.</summary>
    public PositionKind Kind => (PositionKind)KindValue;

    public bool IsMainMercenary => (Flags & PositionEntityFlags.IsMainMercenary) != 0;
    public bool IsPlayerOwned => (Flags & PositionEntityFlags.IsPlayerOwned) != 0;
    public bool FromOriginTransform => (Flags & PositionEntityFlags.FromOriginTransform) != 0;
}

/// <summary>
/// Container classification for
/// <see cref="PositionedEntityRecord.KindValue"/>. Numeric values
/// are part of the C ABI surface — never reassign.
/// </summary>
public enum PositionKind : uint
{
    /// <summary>
    /// Active playable character. Read from
    /// <c>TransformSaveData._fieldSaveDataList[0]._position</c>.
    /// At most one record of this kind per save.
    /// </summary>
    ActiveChar = 0,

    /// <summary>
    /// A mercenary / mount / inactive-playable. Position from
    /// <c>_spawnPosition</c>; yaw from <c>_spawnYaw</c>. Only emitted
    /// when <c>_spawnPosition</c> is present (76 of 96 mercenaries
    /// in slot103 carry a present position).
    /// </summary>
    Mercenary = 1,

    /// <summary>
    /// A field gimmick (top-level <c>FieldGimmickSaveData</c>).
    /// Position decoded from the 40-byte <c>Transform</c> inline
    /// bytes (<c>_transform</c> field 11 preferred, falls back to
    /// <c>_originSpawnTransform</c> field 12 — see
    /// <see cref="PositionEntityFlags.FromOriginTransform"/>).
    /// </summary>
    Gimmick = 2,
}

/// <summary>
/// Bit constants for <see cref="PositionedEntityRecord.Flags"/>.
/// Mirrors the Rust-side <c>position_flags</c> module.
/// </summary>
public static class PositionEntityFlags
{
    /// <summary>
    /// <c>MercenarySaveData._isMainMercenary = true</c> — the
    /// currently-summoned mount / main companion. Always 0 for non-
    /// <see cref="PositionKind.Mercenary"/> kinds.
    /// </summary>
    public const uint IsMainMercenary = 1u << 0;

    /// <summary>
    /// Item / entity belongs to a playable character. Same semantics
    /// as <see cref="ItemRecordFlags.IsPlayerOwned"/>. Always set for
    /// <see cref="PositionKind.ActiveChar"/>; always clear for
    /// <see cref="PositionKind.Gimmick"/>.
    /// </summary>
    public const uint IsPlayerOwned = 1u << 1;

    /// <summary>
    /// <see cref="PositionKind.Gimmick"/> used
    /// <c>_originSpawnTransform</c> (field 12) instead of
    /// <c>_transform</c> (field 11). The latter is the "current"
    /// transform after a gimmick has moved or been interacted with;
    /// the former is the spawn-time transform. In slot103 ~94% of
    /// position-bearing gimmicks have this flag set.
    /// </summary>
    public const uint FromOriginTransform = 1u << 2;
}
