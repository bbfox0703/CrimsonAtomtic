using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.RustInterop;

/// <summary>
/// Loads a Crimson Desert save file into a <see cref="SaveSummary"/>.
/// The production implementation, <see cref="NativeSaveLoader"/>, P/Invokes
/// into <c>crimson_rs.dll</c> (built from <c>vendor/crimson-rs</c> with the
/// <c>c_abi</c> Cargo feature).
/// </summary>
public interface ISaveLoader
{
    /// <summary>
    /// Read <paramref name="savePath"/> from disk, parse it, and return a
    /// summary. Throws on malformed input; never returns <c>null</c>.
    /// </summary>
    SaveSummary Load(string savePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the full per-field decode of the block at <paramref name="blockIndex"/>
    /// in <paramref name="savePath"/>. Called lazily by the UI when the
    /// user selects a row in the blocks DataGrid.
    /// </summary>
    BlockDetails LoadBlockDetails(string savePath, int blockIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrite the bytes of a fixed-size scalar field in the
    /// currently-loaded save with <paramref name="bytes"/>. The set is
    /// validated against the field's recorded byte range; mismatched
    /// length or non-scalar kinds throw <see cref="CrimsonSaveException"/>
    /// with a precise error code.
    /// </summary>
    /// <remarks>
    /// Equivalent to calling the path-addressed overload with an empty
    /// path. Requires a prior <see cref="Load"/> call: this acts on the
    /// in-memory handle, not on a transient open. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </remarks>
    void SetScalarField(int blockIndex, int fieldIndex, ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Path-addressed scalar setter — mutates a scalar reachable through a
    /// chain of inline locators and/or list elements descending from a
    /// top-level TOC block. <paramref name="path"/> is the descent
    /// sequence; <paramref name="fieldIndex"/> picks the leaf scalar in
    /// the block reached at the end. An empty <paramref name="path"/> is
    /// identical to the no-path overload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mid-path steps that don't resolve to <c>ObjectLocator</c> (with a
    /// resolved inline child) or <c>ObjectList</c> fail with
    /// <c>NOT_NAVIGABLE (-15)</c>. Leaf-side checks are the same as the
    /// no-path version (<c>NOT_SCALAR</c> / <c>LENGTH_MISMATCH</c> /
    /// <c>OUT_OF_RANGE</c>).
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    void SetScalarField(int blockIndex, ReadOnlySpan<PathStep> path, int fieldIndex, ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Apply many <see cref="ScalarBatchOp"/> mutations in one FFI round
    /// trip, sharing a single post-batch re-decode. All-or-nothing: if
    /// any op fails validation the save body is left exactly as it was
    /// before the call, and the thrown
    /// <see cref="CrimsonSaveException"/> carries
    /// <see cref="CrimsonSaveException.FailedOpIndex"/> pinpointing the
    /// offending op. An empty <paramref name="ops"/> list is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-op validation rules match the single-op
    /// <see cref="SetScalarField(int, ReadOnlySpan{PathStep}, int, ReadOnlySpan{byte})"/>
    /// exactly — same <c>NOT_SCALAR</c> / <c>LENGTH_MISMATCH</c> /
    /// <c>OUT_OF_RANGE</c> / <c>NOT_NAVIGABLE</c> codes. The batch path
    /// exists for performance: applying N ops one-at-a-time pays
    /// N × <c>decode_blocks</c> cost (~5 s for the 168-op "Fill stacks"
    /// flow on the 1112-block save), while the batch amortises to
    /// O(N + block_count) with a single re-decode at the end.
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    void SetScalarFieldsBatch(IReadOnlyList<ScalarBatchOp> ops);

    /// <summary>
    /// Write the currently-loaded save (with any in-memory edits) to
    /// <paramref name="destinationPath"/>. The header's nonce is reused
    /// and HMAC / LZ4 / ChaCha20 are rebuilt against the modified body
    /// so the on-disk layout matches what the game produced.
    /// </summary>
    /// <remarks>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </remarks>
    void WriteToFile(string destinationPath);
}
