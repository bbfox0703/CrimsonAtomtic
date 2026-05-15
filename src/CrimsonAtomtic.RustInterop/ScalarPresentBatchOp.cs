namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One scalar-presence mutation in an
/// <see cref="ISaveLoader.SetScalarFieldsPresentBatch(System.Collections.Generic.IReadOnlyList{ScalarPresentBatchOp})"/>
/// call. Mirrors <c>CrimsonScalarPresentBatchOp</c> on the Rust side at
/// the fields level; the FFI marshalling layer packs these into one
/// contiguous arena and crosses the boundary with a single P/Invoke.
/// </summary>
/// <param name="BlockIndex">
/// Top-level TOC block index.
/// </param>
/// <param name="Path">
/// Descent steps from the top block. May be empty for a top-level
/// scalar; same semantics as the empty-path overload of
/// <see cref="ISaveLoader.SetScalarFieldPresent(int, System.ReadOnlySpan{PathStep}, int, bool, System.ReadOnlySpan{byte})"/>.
/// </param>
/// <param name="FieldIndex">
/// Leaf field index inside the block reached at the end of
/// <paramref name="Path"/>. Must point at a fixed-size scalar
/// (<c>meta_kind 0</c> or <c>2</c>); non-scalar leaves are rejected
/// per-op with <c>NOT_SCALAR_FIELD_KIND</c>.
/// </param>
/// <param name="MakePresent">
/// <c>true</c> flips the field's mask bit on (and writes
/// <paramref name="InitialBytes"/> as the scalar value);
/// <c>false</c> flips it off (and <paramref name="InitialBytes"/>
/// is ignored, may be <c>null</c> or empty).
/// </param>
/// <param name="InitialBytes">
/// When <paramref name="MakePresent"/> is <c>true</c>, must match the
/// field's recorded <c>meta_size</c> exactly (e.g. 8 bytes for a u64
/// <c>_completedTime</c>). Length mismatches are rejected per-op with
/// <c>LENGTH_MISMATCH</c>. When <paramref name="MakePresent"/> is
/// <c>false</c> this is ignored.
/// </param>
/// <remarks>
/// The arrays are read-only as far as the FFI is concerned but the
/// record holds them by reference, not by copy — callers must not
/// mutate either array between constructing the op and the
/// <see cref="ISaveLoader.SetScalarFieldsPresentBatch"/> call returning.
/// </remarks>
public readonly record struct ScalarPresentBatchOp(
    int BlockIndex,
    PathStep[] Path,
    int FieldIndex,
    bool MakePresent,
    byte[] InitialBytes);
