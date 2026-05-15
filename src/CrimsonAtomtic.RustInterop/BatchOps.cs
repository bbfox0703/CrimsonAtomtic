namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One <c>_state-flip-style</c> presence-toggle in a
/// <see cref="ISaveLoader.SetScalarFieldsPresentBatch(System.Collections.Generic.IReadOnlyList{ScalarPresentBatchOp})"/>
/// call. Mirrors <c>CrimsonScalarPresentBatchOp</c> on the Rust side at
/// the field-shape level; the FFI marshalling layer packs these into
/// one contiguous arena and crosses the boundary with a single P/Invoke.
/// </summary>
/// <param name="BlockIndex">Top-level TOC block index.</param>
/// <param name="Path">
/// Descent steps from the top block to the parent of the leaf scalar.
/// May be empty for a top-level field; same semantics as the no-path
/// overload of <see cref="ISaveLoader.SetScalarFieldPresent"/>.
/// </param>
/// <param name="FieldIndex">
/// Leaf field index inside the block reached at the end of
/// <paramref name="Path"/>. Must point at a fixed-size scalar (meta_kind
/// 0 or 2). Lists / locators / inline-bytes are rejected with
/// <c>NOT_SCALAR_FIELD_KIND (-18)</c>.
/// </param>
/// <param name="MakePresent">
/// <c>true</c> = flip the mask bit on (and decode <see cref="Bytes"/> as
/// the field's payload); <c>false</c> = flip off (the existing payload
/// bytes are dropped — <see cref="Bytes"/> is ignored).
/// </param>
/// <param name="Bytes">
/// When <see cref="MakePresent"/> is <c>true</c>, must equal the field's
/// recorded byte width (e.g. 8 for u64). Ignored when
/// <see cref="MakePresent"/> is <c>false</c> — pass
/// <c>Array.Empty&lt;byte&gt;()</c>.
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
    byte[] Bytes);

/// <summary>
/// One element drop in a
/// <see cref="ISaveLoader.ListRemoveElementsBatch(System.Collections.Generic.IReadOnlyList{ListRemoveBatchOp})"/>
/// call. Mirrors <c>CrimsonListRemoveBatchOp</c>.
/// </summary>
/// <param name="BlockIndex">Top-level TOC block index.</param>
/// <param name="Path">
/// Descent steps from the top block to the block that owns the list.
/// May be empty.
/// </param>
/// <param name="FieldIndex">
/// Index of the <c>object_list</c> field in the block reached at the
/// end of <paramref name="Path"/>. Non-list fields are rejected with
/// <c>NOT_SCALAR (-12)</c>.
/// </param>
/// <param name="ElementIndex">
/// Element to remove. <b>Important:</b> ops are applied in input order
/// and earlier removes shift later indexes within the same list.
/// Pre-sort ops targeting the same list by descending element index
/// before submitting the batch.
/// </param>
/// <remarks>
/// <para>
/// Ordering example: dropping elements [3, 7] from the same list — sort
/// to [7, 3] so the index-3 removal doesn't move the index-7 element
/// down to index 6 by the time we try to remove it. Removes targeting
/// different lists / blocks are independent and don't need pre-sorting
/// against each other.
/// </para>
/// </remarks>
public readonly record struct ListRemoveBatchOp(
    int BlockIndex,
    PathStep[] Path,
    int FieldIndex,
    int ElementIndex);
