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
    /// Read the loaded handle's monotonic mutation counter. The counter
    /// starts at 0 on <see cref="Load"/> and bumps by exactly 1 on every
    /// successful mutation through this interface (<c>SetScalarField</c>,
    /// <c>ListRemoveElement</c>, batch variants, …). Pure reads
    /// (<see cref="LoadBlockDetails"/>, <see cref="ListInventoryItems"/>,
    /// …) DO NOT bump it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pair with <see cref="ListInventoryItems"/>'s out-version stamp
    /// for cheap O(1) staleness detection on cached snapshots. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    ulong GetMutationVersion();

    /// <summary>
    /// Open a deferred-redecode batch on the currently-loaded save.
    /// Within an open batch, every length-changing mutation entry
    /// point (<c>ListCloneElement</c>, <c>ListRemoveElement</c>,
    /// <c>SetScalarFieldPresent</c>, <c>DynamicArraySetU32Elements</c>,
    /// batch variants, …) skips the per-call encode + parse +
    /// <c>decode_blocks</c> tail and mutates the in-memory tree
    /// directly. Scalar entry points update typed
    /// <c>ScalarValue</c>s in place. The matching
    /// <see cref="EndDeferredRedecode"/> commits everything with
    /// one decode pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Batches do not nest. Pairs <b>must</b> match — open + commit
    /// or open + abort. <see cref="WriteToFile"/> is rejected while a
    /// batch is open (BATCH_IN_PROGRESS); end / abort first.
    /// </para>
    /// <para>
    /// Most callers should prefer <see cref="RunDeferred"/> over the
    /// raw triple — it auto-aborts on exception.
    /// </para>
    /// </remarks>
    void BeginDeferredRedecode();

    /// <summary>
    /// Commit the deferred batch — runs one
    /// <c>encode + parse + decode_blocks</c> pass over the accumulated
    /// tree and bumps the mutation version exactly once. Throws
    /// <see cref="CrimsonSaveException"/> with code
    /// <c>MUTATION_INVALID</c> when the accumulated tree fails to
    /// encode or re-parse (handle is auto-rolled-back to the
    /// pre-begin snapshot).
    /// </summary>
    void EndDeferredRedecode();

    /// <summary>
    /// Discard every in-batch mutation; restore the handle to its
    /// pre-begin state. Mutation version reverts to its pre-begin
    /// value.
    /// </summary>
    void AbortDeferredRedecode();

    /// <summary>
    /// True iff a <see cref="BeginDeferredRedecode"/> batch is open
    /// on the currently-loaded save.
    /// </summary>
    bool IsDeferredRedecodeOpen();

    /// <summary>
    /// Convenience wrapper around begin / end / abort:
    /// <list type="bullet">
    ///   <item>Calls <see cref="BeginDeferredRedecode"/>.</item>
    ///   <item>Invokes <paramref name="body"/>.</item>
    ///   <item>On exception, calls <see cref="AbortDeferredRedecode"/>
    ///     and rethrows.</item>
    ///   <item>On normal completion, calls
    ///     <see cref="EndDeferredRedecode"/> — which itself may throw
    ///     <c>MUTATION_INVALID</c>.</item>
    /// </list>
    /// For partial-success workflows that want to KEEP already-applied
    /// work when a per-op failure mid-batch fires, swallow per-op
    /// exceptions inside <paramref name="body"/> so it returns
    /// normally — that path lets the commit land.
    /// </summary>
    void RunDeferred(Action body);

    /// <summary>
    /// Flat-list every item slot across every <c>InventorySaveData</c>
    /// block in the currently-loaded save. One FFI call replaces the
    /// 18-container × N-item nesting walk callers used to do manually
    /// (<c>InventorySaveData → _inventorylist[N] → _itemList[M]</c>).
    /// </summary>
    /// <param name="version">
    /// Out parameter — receives the handle's mutation counter at the
    /// moment the list was read. Pair with
    /// <see cref="GetMutationVersion"/> later to detect whether the
    /// snapshot's positional fields
    /// (<see cref="InventoryItemRecord.BlockIndex"/> /
    /// <see cref="InventoryItemRecord.InventoryElementIndex"/> /
    /// <see cref="InventoryItemRecord.ItemElementIndex"/>) are still
    /// valid — they go stale on the next length-changing mutation.
    /// </param>
    /// <returns>
    /// Flat list of every slot. Empty when the save has zero inventory
    /// items. The records are pure data — safe to retain across
    /// non-mutating calls, but the positional indexes go stale after
    /// any list edit (see version contract).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Performance: O(total items + 18 containers). Under 1 ms on a
    /// 1.07-era save with 543 items.
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    IReadOnlyList<InventoryItemRecord> ListInventoryItems(out ulong version);

    /// <summary>
    /// Flat-list every <c>CharacterKey</c>-typed reference across every
    /// top-level block + every ObjectList / Locator descendant in the
    /// currently-loaded save. One FFI call replaces the recursive
    /// schema-tagged walk callers would otherwise hand-roll on the
    /// C# side.
    /// </summary>
    /// <param name="version">
    /// Out parameter — receives the handle's mutation counter at the
    /// moment the list was read. Pair with
    /// <see cref="GetMutationVersion"/> later to detect whether the
    /// snapshot's <see cref="CharacterRefRecord.BlockIndex"/> values
    /// are still valid — they go stale on any block-list mutation.
    /// </param>
    /// <returns>
    /// Flat list of every <c>CharacterKey</c> occurrence — one row per
    /// present scalar field; one row per element of a
    /// <c>DynamicArray&lt;CharacterKey&gt;</c>. Empty when no save
    /// field has the <c>CharacterKey</c> schema type. Caveat: rows are
    /// best-effort linkage — the same key can mean different
    /// concrete entities under different schema field roles. Records
    /// are pure data — safe to retain across non-mutating calls, but
    /// the <see cref="CharacterRefRecord.BlockIndex"/> goes stale
    /// after any block-list edit.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Performance: O(total fields). Sub-millisecond on a 1.07-era
    /// save with several thousand blocks.
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    IReadOnlyList<CharacterRefRecord> ListCharacterRefs(out ulong version);

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

    // ── Length-changing edits (PR B) ───────────────────────────────────────
    //
    // The first three (Remove / Clone / SetScalarFieldPresent) cover the
    // user-facing UX gestures the editor needs today:
    //   - Remove an item from a bag → ListRemoveElement
    //   - Add an item by duplicating an existing one → ListCloneElement +
    //     SetScalarField (patch _itemKey / _stackCount on the clone)
    //   - Mark a challenge complete → SetScalarFieldPresent (_completedTime
    //     absent → present with a u64 timestamp)
    // The remaining two (MakeEmptyElementBytes / ListInsertElement) cover
    // the "add a brand-new element of any class" path where no similar
    // element exists to clone from.

    /// <summary>
    /// Drop element <paramref name="elementIndex"/> from an
    /// <c>object_list</c> field reached by
    /// <c>(blockIndex, path, fieldIndex)</c>. The list's count and its
    /// variant-specific header bytes are rewritten in place and the body
    /// is re-emitted + re-parsed so subsequent reads see the new layout.
    /// </summary>
    /// <remarks>
    /// Variants observed in 1.06 saves all use
    /// <c>zero1_count_u24</c>; the only unsupported variant is
    /// <c>marker_run_plus_zeros</c>, which is rejected with
    /// <c>LIST_VARIANT_UNSUPPORTED (-17)</c>. On any error the in-memory
    /// save body is left exactly as it was before the call.
    /// </remarks>
    void ListRemoveElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int elementIndex);

    /// <summary>
    /// Insert a byte-identical copy of element <paramref name="sourceIndex"/>
    /// at position <paramref name="destinationIndex"/> in the same
    /// <c>object_list</c>. <c>destinationIndex</c> may be 0..=count
    /// (inclusive on both ends; equal to count means append).
    /// </summary>
    /// <remarks>
    /// The clone is byte-identical to the source — callers typically
    /// follow up with <see cref="SetScalarField(int, ReadOnlySpan{PathStep}, int, ReadOnlySpan{byte})"/>
    /// to patch a few scalar fields (<c>_itemKey</c>, <c>_stackCount</c>,
    /// <c>_slotNo</c>, …) so the clone represents a distinct entity.
    /// </remarks>
    void ListCloneElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int sourceIndex,
        int destinationIndex);

    /// <summary>
    /// Flip the presence bit of a fixed-size scalar field. When making
    /// the field present, <paramref name="initialBytes"/> must equal
    /// the field's <c>meta_size</c> and is decoded into the field's
    /// declared scalar type. When making the field absent,
    /// <paramref name="initialBytes"/> is ignored (pass
    /// <c>ReadOnlySpan&lt;byte&gt;.Empty</c>).
    /// </summary>
    /// <remarks>
    /// Restricted to scalar fields (<c>meta_kind 0</c> or <c>2</c>).
    /// Toggling presence on a list / locator / inline-bytes field
    /// returns <c>NOT_SCALAR_FIELD_KIND (-18)</c>; the template-builder
    /// surface (<see cref="MakeEmptyElementBytes"/> +
    /// <see cref="ListInsertElement"/>) handles those richer cases.
    /// </remarks>
    void SetScalarFieldPresent(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        bool makePresent,
        ReadOnlySpan<byte> initialBytes);

    /// <summary>
    /// Apply many <see cref="ScalarPresentBatchOp"/> presence-flips in
    /// one FFI round trip, sharing a single post-batch re-emit + re-decode.
    /// All-or-nothing: if any op fails validation the save body is left
    /// exactly as it was before the call, and the thrown
    /// <see cref="CrimsonSaveException"/> carries
    /// <see cref="CrimsonSaveException.FailedOpIndex"/> pinpointing the
    /// offending op. An empty <paramref name="ops"/> list is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-op validation rules match the single-op
    /// <see cref="SetScalarFieldPresent"/> exactly — same
    /// <c>NOT_SCALAR_FIELD_KIND</c> / <c>LENGTH_MISMATCH</c> /
    /// <c>OUT_OF_RANGE</c> / <c>NOT_NAVIGABLE</c> codes.
    /// </para>
    /// <para>
    /// This is the <b>perf-critical</b> path for bulk presence flips.
    /// Each per-call <see cref="SetScalarFieldPresent"/> re-emits the
    /// entire body and re-decodes every block (~1 s on a 1100-block
    /// save). Promoting <c>_completedTime</c> on 1300+ challenges
    /// one-at-a-time costs ~20 minutes; the batch amortizes to a single
    /// re-emit + re-decode (~seconds).
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    void SetScalarFieldsPresentBatch(IReadOnlyList<ScalarPresentBatchOp> ops);

    /// <summary>
    /// Apply many <see cref="ListRemoveBatchOp"/> element drops in one
    /// FFI round trip. All-or-nothing on validation failure (same
    /// <see cref="CrimsonSaveException.FailedOpIndex"/> protocol as the
    /// scalar batches).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Caller pre-sorts.</b> Ops are applied in input order and
    /// earlier removes shift later element indexes within the same
    /// list. Sort by <c>(blockIndex, listPath, descending elementIndex)</c>
    /// before calling.
    /// </para>
    /// <para>
    /// Single re-emit + re-decode at the end regardless of <c>ops</c>
    /// size — replaces what was N × <c>ListRemoveElement</c> with
    /// N × decode_blocks.
    /// </para>
    /// <para>
    /// Requires a prior <see cref="Load"/> call. Throws
    /// <see cref="InvalidOperationException"/> when no save is loaded.
    /// </para>
    /// </remarks>
    void ListRemoveElementsBatch(IReadOnlyList<ListRemoveBatchOp> ops);

    /// <summary>
    /// Wholesale-replace the contents of a <c>dynamic_array&lt;u32&gt;</c>
    /// field reached at <c>(blockIndex, path, fieldIndex)</c>. The
    /// existing element bytes are dropped and replaced with
    /// <paramref name="newElements"/> (each element written little-endian);
    /// the variant header's count slot is rewritten to match. The body
    /// is re-emitted + re-decoded once at the end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To append/insert: read the current elements via
    /// <see cref="LoadBlockDetails"/>, build the desired sequence in
    /// the caller, then pass the whole sequence here.
    /// </para>
    /// <para>
    /// Restricted to <c>u32</c> element width (the schema's
    /// <c>meta_size == 4</c> case). Other widths are rejected with
    /// <c>LENGTH_MISMATCH</c>.
    /// </para>
    /// <para>
    /// Errors: <c>NOT_SCALAR</c> when the field isn't a dynamic_array;
    /// <c>LENGTH_MISMATCH</c> when meta_size != 4;
    /// <c>OUT_OF_RANGE</c> when newCount exceeds the variant header's
    /// count slot (<c>0x10000</c> for compact / prefix / marker variants);
    /// <c>BODY_PARSE</c> when the variant header is malformed or unknown.
    /// </para>
    /// </remarks>
    void DynamicArraySetU32Elements(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        ReadOnlySpan<uint> newElements);

    /// <summary>
    /// Read the contents of a <c>dynamic_array&lt;u32&gt;</c> field at
    /// <c>(blockIndex, path, fieldIndex)</c> as a flat <see cref="uint"/>
    /// array. Returns an empty array when the field is empty.
    /// </summary>
    /// <remarks>
    /// Same field validation as
    /// <see cref="DynamicArraySetU32Elements"/> (must be a dynamic_array
    /// of u32). Internally uses the standard two-call buffer pattern
    /// against the C ABI.
    /// </remarks>
    uint[] DynamicArrayGetU32Elements(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex);

    /// <summary>
    /// Wholesale-replace an <c>inline_bytes</c> field's payload (schema
    /// <c>meta_kind == 1</c>: a <c>u32 count</c> header followed by
    /// <c>count * meta_size</c> payload bytes). Motivating use case is
    /// renaming length-prefixed UTF-8 string fields such as
    /// <c>MercenarySaveData._mercenaryName</c>; the same surface works
    /// for any homogeneous-element-width inline array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Caller passes raw payload bytes; the FFI derives
    /// <c>count = bytes.Length / meta_size</c> and rewrites the count
    /// header. The standard length-changing re-emit pipeline cascades
    /// all downstream offsets so callers don't need to know byte layout
    /// beyond the field itself.
    /// </para>
    /// <para>
    /// Absent field is promoted to present (mask bit set) +
    /// <paramref name="newBytes"/> written. Present field has its
    /// existing payload overwritten. Empty <paramref name="newBytes"/>
    /// writes <c>count = 0</c> + empty payload — does NOT make absent.
    /// </para>
    /// <para>
    /// Errors: <c>NOT_INLINE_BYTES</c> when the field's <c>meta_kind</c>
    /// isn't 1; <c>LENGTH_MISMATCH</c> when <c>newBytes.Length</c> isn't
    /// a multiple of <c>meta_size</c>; <c>OUT_OF_RANGE</c> when the
    /// derived count exceeds <c>uint.MaxValue</c>.
    /// </para>
    /// </remarks>
    void SetInlineBytesField(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        ReadOnlySpan<byte> newBytes);

    /// <summary>
    /// Produce the minimal valid bytes for a list element of
    /// <paramref name="classIndex"/>: a wrapper with an all-zero mask
    /// (every field absent) and an empty inline payload. Total size is
    /// <c>mbc + 25</c> bytes, where <c>mbc</c> is the class's mask byte
    /// count.
    /// </summary>
    /// <remarks>
    /// The returned bytes can be passed straight to
    /// <see cref="ListInsertElement"/>. The standard editor workflow
    /// then populates fields one-by-one via
    /// <see cref="SetScalarFieldPresent"/> + the existing
    /// <see cref="SetScalarField(int, ReadOnlySpan{PathStep}, int, ReadOnlySpan{byte})"/>.
    /// </remarks>
    byte[] MakeEmptyElementBytes(int classIndex);

    /// <summary>
    /// Insert a caller-supplied list-element bytes blob into an
    /// <c>object_list</c> at position <paramref name="insertAt"/>
    /// (0..=count). The bytes are decoded against the loaded schema
    /// before insertion; malformed blobs throw
    /// <see cref="CrimsonSaveException"/> with code
    /// <c>BODY_PARSE (-9)</c> and the handle is left untouched.
    /// </summary>
    /// <remarks>
    /// Use <see cref="MakeEmptyElementBytes"/> to construct the blob
    /// for a brand-new element of an arbitrary class, or extract bytes
    /// from an existing element via
    /// <see cref="LoadBlockDetails"/> + body offsets when copying from
    /// a different save. For clone-and-edit within the same save,
    /// <see cref="ListCloneElement"/> is more efficient.
    /// </remarks>
    void ListInsertElement(
        int blockIndex,
        ReadOnlySpan<PathStep> path,
        int fieldIndex,
        int insertAt,
        ReadOnlySpan<byte> bytes);
}
