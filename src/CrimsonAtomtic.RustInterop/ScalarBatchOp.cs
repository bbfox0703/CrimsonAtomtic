namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One scalar mutation in an
/// <see cref="ISaveLoader.SetScalarFieldsBatch(System.Collections.Generic.IReadOnlyList{ScalarBatchOp})"/>
/// call. Mirrors <c>CrimsonScalarBatchOp</c> on the Rust side at the
/// fields level; the FFI marshalling layer packs these into one
/// contiguous arena and crosses the boundary with a single P/Invoke.
/// </summary>
/// <param name="BlockIndex">
/// Top-level TOC block index.
/// </param>
/// <param name="Path">
/// Descent steps from the top block. May be empty for a top-level
/// scalar; same semantics as the empty-path overload of
/// <see cref="ISaveLoader.SetScalarField(int, System.ReadOnlySpan{PathStep}, int, System.ReadOnlySpan{byte})"/>.
/// </param>
/// <param name="FieldIndex">
/// Leaf field index inside the block reached at the end of
/// <paramref name="Path"/>. Must point at a fixed-size scalar
/// (<c>FixedPrefix</c> / <c>FixedSuffix</c>).
/// </param>
/// <param name="Bytes">
/// Replacement bytes — must be exactly the leaf field's recorded
/// byte width (e.g. 4 for u32). Length-changing edits are rejected
/// with <c>LENGTH_MISMATCH (-13)</c>.
/// </param>
/// <remarks>
/// The arrays are read-only as far as the FFI is concerned but the
/// record holds them by reference, not by copy — callers must not
/// mutate either array between constructing the op and the
/// <see cref="ISaveLoader.SetScalarFieldsBatch"/> call returning.
/// </remarks>
public readonly record struct ScalarBatchOp(
    int BlockIndex,
    PathStep[] Path,
    int FieldIndex,
    byte[] Bytes);
