namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// One list-element removal in a
/// <see cref="ISaveLoader.ListRemoveElementsBatch(System.Collections.Generic.IReadOnlyList{ListRemoveBatchOp})"/>
/// call. Mirrors <c>CrimsonListRemoveBatchOp</c> on the Rust side at the
/// fields level.
/// </summary>
/// <param name="BlockIndex">
/// Top-level TOC block index containing the <c>object_list</c> field.
/// </param>
/// <param name="Path">
/// Descent steps from the top block. May be empty when the
/// <c>object_list</c> is itself a top-level field on the block.
/// </param>
/// <param name="FieldIndex">
/// Leaf field index pointing at the <c>object_list</c> to mutate.
/// Non-list leaves are rejected per-op with <c>NOT_SCALAR</c>.
/// </param>
/// <param name="ElementIndex">
/// Index of the element to drop, as observed against the list's state
/// at the moment this op is applied. Ops are applied in input order,
/// so earlier removes against the same list shift later indexes —
/// callers must pre-sort ops targeting the same list by descending
/// <see cref="ElementIndex"/> so the indexes stay valid through the
/// batch. See <see cref="ISaveLoader.ListRemoveElementsBatch"/> remarks.
/// </param>
/// <remarks>
/// The path array is read-only as far as the FFI is concerned but the
/// record holds it by reference, not by copy — callers must not mutate
/// it between constructing the op and the
/// <see cref="ISaveLoader.ListRemoveElementsBatch"/> call returning.
/// </remarks>
public readonly record struct ListRemoveBatchOp(
    int BlockIndex,
    PathStep[] Path,
    int FieldIndex,
    int ElementIndex);
