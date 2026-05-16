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
/// bytes, next-prefab/tail-name). 1,105 rows in 1.07. Replaces the
/// PyQt5 editor's hand-maintained <c>dye_slot_counts.json</c>.
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
