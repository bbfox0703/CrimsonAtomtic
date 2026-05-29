using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonAtomtic.RustInterop;

// ─────────────────────────────────────────────────────────────────────────────
//  DyeColorGroupInfo  (pabgb + pabgh — DyeColorGroupInfoKey → named color group)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>DyeColorGroupInfoKey (u32)</c> → named color group (e.g.
/// <c>"Her_Color_Group_I"</c>, <c>"Dem_Color_Group_I/II/III"</c>).
/// Loaded from <c>dyecolorgroupinfo.pabgb</c> + <c>.pabgh</c>. 10
/// rows in 1.07. Drives the Dye editor's color-group dropdown.
/// </summary>
public sealed class NativeDyeColorGroupInfoCatalog : IDisposable
{
    private readonly CrimsonDyeColorGroupInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativeDyeColorGroupInfoCatalog(CrimsonDyeColorGroupInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativeDyeColorGroupInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.DyeColorGroupInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_dye_color_group_info_load_from_bytes(pabgb={pabgb.Length}, pabgh={pabgh.Length}) "
                        + $"failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonDyeColorGroupInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.DyeColorGroupInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_dye_color_group_info_entry_count failed: {NameBuffer.ErrorName(rcCount)}");
                }
                return new NativeDyeColorGroupInfoCatalog(handle, (int)count);
            }
        }
    }

    public string? LookupName(uint colorGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.DyeColorGroupInfoLookupName(
                        _handle, colorGroupKey, buf, bufLen, out required),
                $"crimson_dye_color_group_info_lookup_name({colorGroupKey})");
        }
    }

    public (uint Key, string Name)? GetEntry(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        unsafe
        {
            uint outKey = 0;
            nuint required = 0;
            var rc = NativeMethods.DyeColorGroupInfoGetEntry(_handle, (uint)index,
                out outKey, null, 0, out required);
            if (rc == NativeMethods.OUT_OF_RANGE) return null;
            if (rc != NativeMethods.BUFFER_TOO_SMALL && rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    $"crimson_dye_color_group_info_get_entry({index}) size query failed: "
                    + NameBuffer.ErrorName(rc));
            }
            if (required <= 1) return (outKey, string.Empty);
            var rented = ArrayPool<byte>.Shared.Rent((int)required);
            try
            {
                fixed (byte* b = rented)
                {
                    rc = NativeMethods.DyeColorGroupInfoGetEntry(_handle, (uint)index,
                        out outKey, b, (nuint)rented.Length, out required);
                }
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_dye_color_group_info_get_entry({index}) fill failed: "
                        + NameBuffer.ErrorName(rc));
                }
                return (outKey, Encoding.UTF8.GetString(rented, 0, (int)required - 1));
            }
            finally { ArrayPool<byte>.Shared.Return(rented); }
        }
    }

    /// <summary>
    /// Number of palette positions for <paramref name="colorGroupKey"/>'s
    /// theme. 109 in 1.07 (9 grayscale + 10×10 chromatic). Returns null
    /// when the key isn't in the table.
    /// </summary>
    /// <remarks>
    /// The dye picker is a fixed grid per theme, NOT freeform RGB.
    /// See <c>vendor/crimson-rs/docs/dye-editor-scope.md</c>
    /// §"Recommended C# editor UX".
    /// </remarks>
    public int? PaletteSize(uint colorGroupKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.DyeColorGroupInfoPaletteSize(_handle, colorGroupKey, out var count);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_dye_color_group_info_palette_size({colorGroupKey}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return (int)count;
    }

    /// <summary>
    /// Logical RGBA at the given palette position. The four bytes are
    /// in <c>(R, G, B, A)</c> order — ready to write straight into the
    /// save's <c>_dyeColorR/G/B/A</c> u8 scalars. Returns null when the
    /// key or position is out of range.
    /// </summary>
    public (byte R, byte G, byte B, byte A)? PaletteAt(uint colorGroupKey, int positionIdx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(positionIdx);
        var rc = NativeMethods.DyeColorGroupInfoPaletteAt(
            _handle, colorGroupKey, (uint)positionIdx,
            out var r, out var g, out var b, out var a);
        if (rc == NativeMethods.NOT_FOUND || rc == NativeMethods.OUT_OF_RANGE) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_dye_color_group_info_palette_at({colorGroupKey}, {positionIdx}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return (r, g, b, a);
    }

    /// <summary>
    /// Reverse lookup — palette position whose RGB matches
    /// <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>
    /// for <paramref name="colorGroupKey"/>'s theme. Alpha is not part
    /// of the match (every observed position uses 0xFF). Returns null
    /// when the key is unknown or no position is an exact match (i.e.
    /// the save's RGB was set off-grid by a tool like Cheat Engine).
    /// Useful for highlighting which cell a currently-applied dye came
    /// from in the editor's picker grid.
    /// </summary>
    public int? PositionForRgb(uint colorGroupKey, byte r, byte g, byte b)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.DyeColorGroupInfoPositionForRgb(
            _handle, colorGroupKey, r, g, b, out var pos);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_dye_color_group_info_position_for_rgb({colorGroupKey}, " +
                $"{r}, {g}, {b}) failed: " + NameBuffer.ErrorName(rc));
        }
        return (int)pos;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonDyeColorGroupInfoHandle : SafeHandle
{
    public CrimsonDyeColorGroupInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonDyeColorGroupInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonDyeColorGroupInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.DyeColorGroupInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  PartPrefabDyeTexturePalleteInfo
//      (pabgb + pabgh — PartPrefabDyeTexturePalleteKey → material tier
//       with 2–3 sub-records each carrying material/icon/texture/variant)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>PartPrefabDyeTexturePalleteKey (u16, widened to u32)</c> → material
/// tier with sub-records ("cloth"/"leather"/"metal"/…). 11 rows
/// (keys 0..10) in 1.07. Drives the Dye editor's material dropdown.
/// </summary>
public sealed class NativePartPrefabDyeTexturePalleteCatalog : IDisposable
{
    private readonly CrimsonPartPrefabDyeTexturePalleteHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativePartPrefabDyeTexturePalleteCatalog(
        CrimsonPartPrefabDyeTexturePalleteHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    public static NativePartPrefabDyeTexturePalleteCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.PartPrefabDyeTexturePalleteLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_part_prefab_dye_texture_pallete_load_from_bytes(pabgb={pabgb.Length}, "
                        + $"pabgh={pabgh.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonPartPrefabDyeTexturePalleteHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.PartPrefabDyeTexturePalleteEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_part_prefab_dye_texture_pallete_entry_count failed: "
                        + NameBuffer.ErrorName(rcCount));
                }
                return new NativePartPrefabDyeTexturePalleteCatalog(handle, (int)count);
            }
        }
    }

    /// <summary>Number of sub-records in the palette row keyed by <paramref name="paletteKey"/>.</summary>
    public int? LookupSubCount(uint paletteKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.PartPrefabDyeTexturePalleteLookupSubCount(
            _handle, paletteKey, out var count);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_count({paletteKey}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return (int)count;
    }

    public string? LookupSubMaterialName(uint paletteKey, int subIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(subIndex);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeTexturePalleteLookupSubMaterialName(
                        _handle, paletteKey, (uint)subIndex, buf, bufLen, out required),
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_material_name({paletteKey},{subIndex})");
        }
    }

    public string? LookupSubIconPath(uint paletteKey, int subIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(subIndex);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeTexturePalleteLookupSubIconPath(
                        _handle, paletteKey, (uint)subIndex, buf, bufLen, out required),
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_icon_path({paletteKey},{subIndex})");
        }
    }

    public string? LookupSubTexturePath(uint paletteKey, int subIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(subIndex);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeTexturePalleteLookupSubTexturePath(
                        _handle, paletteKey, (uint)subIndex, buf, bufLen, out required),
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_texture_path({paletteKey},{subIndex})");
        }
    }

    public string? LookupSubVariantName(uint paletteKey, int subIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(subIndex);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeTexturePalleteLookupSubVariantName(
                        _handle, paletteKey, (uint)subIndex, buf, bufLen, out required),
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_variant_name({paletteKey},{subIndex})");
        }
    }

    /// <summary>
    /// f32 variant strength; <c>-1.0</c> is the "no variant" sentinel.
    /// Returns <c>null</c> on NOT_FOUND / OUT_OF_RANGE.
    /// </summary>
    public float? LookupSubVariantValue(uint paletteKey, int subIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(subIndex);
        var rc = NativeMethods.PartPrefabDyeTexturePalleteLookupSubVariantValue(
            _handle, paletteKey, (uint)subIndex, out var v);
        if (rc == NativeMethods.NOT_FOUND || rc == NativeMethods.OUT_OF_RANGE) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_part_prefab_dye_texture_pallete_lookup_sub_variant_value({paletteKey},{subIndex}) "
                + $"failed: {NameBuffer.ErrorName(rc)}");
        }
        return v;
    }

    /// <summary>Palette key at insertion index — drives enumeration.</summary>
    public uint? GetEntryKey(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var rc = NativeMethods.PartPrefabDyeTexturePalleteGetEntryKey(
            _handle, (uint)index, out var key);
        if (rc == NativeMethods.OUT_OF_RANGE) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_part_prefab_dye_texture_pallete_get_entry_key({index}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return key;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonPartPrefabDyeTexturePalleteHandle : SafeHandle
{
    public CrimsonPartPrefabDyeTexturePalleteHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonPartPrefabDyeTexturePalleteHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonPartPrefabDyeTexturePalleteHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.PartPrefabDyeTexturePalleteFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  PartPrefabDyeSlotInfo  (pabgb + pabgh — PartPrefabKey → per-prefab dye slots)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>PartPrefabKey (u32)</c> → prefab internal name + per-slot detail
/// (slot count, 3 default-material names, 3 mat indices, 3 mask
/// bytes, next-prefab/tail-name). 1,105 rows in 1.07. Replaces
/// CRIMSON-DESERT-SAVE-EDITOR's hand-maintained <c>dye_slot_counts.json</c>.
///
/// <para>
/// The Dye editor v1 doesn't consume this bridge yet — it walks
/// existing <c>_itemDyeDataList</c> slots directly. Consumption
/// requires the <c>_itemKey → _partPrefabKey</c> cross-reference,
/// which is still open RE upstream.
/// </para>
/// </summary>
public sealed class NativePartPrefabDyeSlotInfoCatalog : IDisposable
{
    private readonly CrimsonPartPrefabDyeSlotInfoHandle _handle;
    private readonly int _entryCount;
    private bool _disposed;

    private NativePartPrefabDyeSlotInfoCatalog(
        CrimsonPartPrefabDyeSlotInfoHandle handle, int entryCount)
    {
        _handle = handle;
        _entryCount = entryCount;
    }

    public int EntryCount
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _entryCount; }
    }

    /// <summary>
    /// Internal accessor used by <see cref="NativeItemPartPrefabCatalog"/>
    /// to pass the underlying handle into the cross-catalog
    /// <c>crimson_item_part_prefab_resolve_dye_slot_count</c> ABI.
    /// </summary>
    internal CrimsonPartPrefabDyeSlotInfoHandle InternalSlotInfoHandle
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _handle; }
    }

    public static NativePartPrefabDyeSlotInfoCatalog LoadFromBytes(
        ReadOnlySpan<byte> pabgb, ReadOnlySpan<byte> pabgh)
    {
        unsafe
        {
            fixed (byte* pb = pabgb)
            fixed (byte* ph = pabgh)
            {
                var rc = NativeMethods.PartPrefabDyeSlotInfoLoadFromBytes(
                    pb, (nuint)pabgb.Length, ph, (nuint)pabgh.Length, out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_part_prefab_dye_slot_info_load_from_bytes(pabgb={pabgb.Length}, "
                        + $"pabgh={pabgh.Length}) failed: {NameBuffer.ErrorName(rc)}");
                }
                var handle = CrimsonPartPrefabDyeSlotInfoHandle.FromOwnedPointer(raw);
                var rcCount = NativeMethods.PartPrefabDyeSlotInfoEntryCount(handle, out var count);
                if (rcCount != NativeMethods.OK)
                {
                    handle.Dispose();
                    throw new CrimsonSaveException(rcCount,
                        $"crimson_part_prefab_dye_slot_info_entry_count failed: "
                        + NameBuffer.ErrorName(rcCount));
                }
                return new NativePartPrefabDyeSlotInfoCatalog(handle, (int)count);
            }
        }
    }

    /// <summary>
    /// Dye-slot count for the prefab keyed by <paramref name="prefabKey"/>.
    /// Returns <c>null</c> when the prefab isn't in the table.
    /// </summary>
    public int? LookupSlotCount(uint prefabKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.PartPrefabDyeSlotInfoLookupSlotCount(
            _handle, prefabKey, out var count);
        if (rc == NativeMethods.NOT_FOUND) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_part_prefab_dye_slot_info_lookup_slot_count({prefabKey}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return (int)count;
    }

    public string? LookupPrefabName(uint prefabKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeSlotInfoLookupPrefabName(
                        _handle, prefabKey, buf, bufLen, out required),
                $"crimson_part_prefab_dye_slot_info_lookup_prefab_name({prefabKey})");
        }
    }

    public string? LookupSlotDefaultMaterial(uint prefabKey, int slotIndex, int matIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(matIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(matIndex, 2);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeSlotInfoLookupSlotDefaultMaterial(
                        _handle, prefabKey, (uint)slotIndex, (uint)matIndex,
                        buf, bufLen, out required),
                $"crimson_part_prefab_dye_slot_info_lookup_slot_default_material({prefabKey},{slotIndex},{matIndex})");
        }
    }

    public string? LookupSlotTailName(uint prefabKey, int slotIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        unsafe
        {
            return NameBuffer.ReadString(
                (byte* buf, nuint bufLen, out nuint required) =>
                    NativeMethods.PartPrefabDyeSlotInfoLookupSlotTailName(
                        _handle, prefabKey, (uint)slotIndex, buf, bufLen, out required),
                $"crimson_part_prefab_dye_slot_info_lookup_slot_tail_name({prefabKey},{slotIndex})");
        }
    }

    /// <summary>3 raw material-index bytes for a slot.</summary>
    public byte[]? LookupSlotMatIndices(uint prefabKey, int slotIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        var buf = new byte[3];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var rc = NativeMethods.PartPrefabDyeSlotInfoLookupSlotMatIndices(
                    _handle, prefabKey, (uint)slotIndex, p);
                if (rc == NativeMethods.NOT_FOUND || rc == NativeMethods.OUT_OF_RANGE) return null;
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_part_prefab_dye_slot_info_lookup_slot_mat_indices({prefabKey},{slotIndex}) "
                        + $"failed: {NameBuffer.ErrorName(rc)}");
                }
            }
        }
        return buf;
    }

    /// <summary>3 raw mask bytes for a slot.</summary>
    public byte[]? LookupSlotMask(uint prefabKey, int slotIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        var buf = new byte[3];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var rc = NativeMethods.PartPrefabDyeSlotInfoLookupSlotMask(
                    _handle, prefabKey, (uint)slotIndex, p);
                if (rc == NativeMethods.NOT_FOUND || rc == NativeMethods.OUT_OF_RANGE) return null;
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        $"crimson_part_prefab_dye_slot_info_lookup_slot_mask({prefabKey},{slotIndex}) "
                        + $"failed: {NameBuffer.ErrorName(rc)}");
                }
            }
        }
        return buf;
    }

    public uint? GetEntryKey(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var rc = NativeMethods.PartPrefabDyeSlotInfoGetEntryKey(
            _handle, (uint)index, out var key);
        if (rc == NativeMethods.OUT_OF_RANGE) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_part_prefab_dye_slot_info_get_entry_key({index}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return key;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonPartPrefabDyeSlotInfoHandle : SafeHandle
{
    public CrimsonPartPrefabDyeSlotInfoHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonPartPrefabDyeSlotInfoHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonPartPrefabDyeSlotInfoHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.PartPrefabDyeSlotInfoFree(handle);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  ItemPartPrefab — ItemKey → list of PartPrefabKey via the 3-table join
//      (iteminfo + stringinfo + partprefabdyeslotinfo), plus the
//      ResolveDyeSlotCount convenience wrapper.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of <see cref="NativeItemPartPrefabCatalog.ResolveDyeSlotCount"/>.
/// Numeric values pin to the C ABI constants in
/// <c>vendor/crimson-rs/src/c_abi/item_part_prefab.rs::resolve_dye_slot_count_source</c>
/// — never reassign.
/// </summary>
public enum DyeSlotCountSource : uint
{
    /// <summary>Authoritative count from the iteminfo→partprefab chain.</summary>
    Direct = 0,

    /// <summary>
    /// Item has no entry in the iteminfo/partprefab join — usually a body-
    /// type variant that shares the human-male slot layout but isn't itself
    /// listed. ~76% of dyeable items in 1.07 land here. Slot count is 0;
    /// caller should fall back to a curated default or display "unknown".
    /// </summary>
    NotResolvedNoPartPrefab = 1,

    /// <summary>
    /// Item mapped to a partprefab, but the partprefab isn't in the slot-
    /// info table. Defensive — shouldn't happen given how the join is built.
    /// </summary>
    NotResolvedNoSlotInfo = 2,
}

/// <summary>
/// <c>ItemKey → list&lt;PartPrefabKey&gt;</c> via the 3-table join
/// (<c>iteminfo</c> + <c>stringinfo</c> + <c>partprefabdyeslotinfo</c>) plus
/// the <c>ResolveDyeSlotCount</c> one-shot wrapper that chains the
/// partprefabdyeslotinfo lookup. Replaces CRIMSON-DESERT-SAVE-EDITOR's
/// hand-maintained <c>dye_slot_counts.json</c> end-to-end — the C# Dye
/// editor uses this to surface per-item slot counts at scan time and
/// drive the "Add Dye" slot-picker.
/// </summary>
public sealed class NativeItemPartPrefabCatalog : IDisposable
{
    private readonly CrimsonItemPartPrefabHandle _handle;
    private bool _disposed;

    private NativeItemPartPrefabCatalog(CrimsonItemPartPrefabHandle handle)
    {
        _handle = handle;
    }

    internal CrimsonItemPartPrefabHandle Handle
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _handle; }
    }

    public static NativeItemPartPrefabCatalog LoadFromBytes(
        ReadOnlySpan<byte> iteminfoPabgb,
        ReadOnlySpan<byte> stringinfoPabgb,
        ReadOnlySpan<byte> partprefabPabgb,
        ReadOnlySpan<byte> partprefabPabgh)
    {
        unsafe
        {
            fixed (byte* pi = iteminfoPabgb)
            fixed (byte* ps = stringinfoPabgb)
            fixed (byte* pb = partprefabPabgb)
            fixed (byte* ph = partprefabPabgh)
            {
                var rc = NativeMethods.ItemPartPrefabLoadFromBytes(
                    pi, (nuint)iteminfoPabgb.Length,
                    ps, (nuint)stringinfoPabgb.Length,
                    pb, (nuint)partprefabPabgb.Length,
                    ph, (nuint)partprefabPabgh.Length,
                    out var raw);
                if (rc != NativeMethods.OK)
                {
                    throw new CrimsonSaveException(rc,
                        "crimson_item_part_prefab_load_from_bytes failed: "
                        + NameBuffer.ErrorName(rc));
                }
                return new NativeItemPartPrefabCatalog(
                    CrimsonItemPartPrefabHandle.FromOwnedPointer(raw));
            }
        }
    }

    /// <summary>
    /// Number of items with at least one resolvable partprefab key.
    /// Useful for diagnostics — divide by iteminfo's total item count
    /// to get a coverage estimate.
    /// </summary>
    public int ResolvedItemCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var rc = NativeMethods.ItemPartPrefabResolvedItemCount(_handle, out var count);
            if (rc != NativeMethods.OK)
            {
                throw new CrimsonSaveException(rc,
                    "crimson_item_part_prefab_resolved_item_count failed: "
                    + NameBuffer.ErrorName(rc));
            }
            return (int)count;
        }
    }

    /// <summary>
    /// First partprefab key for <paramref name="itemKey"/>, or
    /// <c>null</c> when the item has no resolvable partprefab. The
    /// returned key feeds <see cref="NativePartPrefabDyeSlotInfoCatalog"/>
    /// lookups (per-slot default material names, mat-indices, mask).
    /// </summary>
    public uint? LookupFirstPrefabKey(uint itemKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rc = NativeMethods.ItemPartPrefabLookupCount(_handle, itemKey, out var count);
        if (rc == NativeMethods.NOT_FOUND || count == 0) return null;
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_item_part_prefab_lookup_count(item_key={itemKey}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        rc = NativeMethods.ItemPartPrefabLookupKeyAt(_handle, itemKey, 0, out var key);
        if (rc != NativeMethods.OK) return null;
        return key;
    }

    /// <summary>
    /// One-shot "how many dye slots does this item have?". Returns the
    /// resolved count (or 0) plus the source enum that tells the caller
    /// whether to trust the result.
    /// </summary>
    public (int SlotCount, DyeSlotCountSource Source) ResolveDyeSlotCount(
        uint itemKey, NativePartPrefabDyeSlotInfoCatalog slotInfo)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(slotInfo);
        var rc = NativeMethods.ItemPartPrefabResolveDyeSlotCount(
            _handle, slotInfo.InternalSlotInfoHandle, itemKey,
            out var slotCount, out var source);
        if (rc != NativeMethods.OK)
        {
            throw new CrimsonSaveException(rc,
                $"crimson_item_part_prefab_resolve_dye_slot_count(item_key={itemKey}) failed: "
                + NameBuffer.ErrorName(rc));
        }
        return ((int)slotCount, (DyeSlotCountSource)source);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }
}

internal sealed class CrimsonItemPartPrefabHandle : SafeHandle
{
    public CrimsonItemPartPrefabHandle() : base(IntPtr.Zero, ownsHandle: true) { }
    public static CrimsonItemPartPrefabHandle FromOwnedPointer(IntPtr ptr)
    {
        var h = new CrimsonItemPartPrefabHandle();
        h.SetHandle(ptr);
        return h;
    }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        NativeMethods.ItemPartPrefabFree(handle);
        return true;
    }
}
