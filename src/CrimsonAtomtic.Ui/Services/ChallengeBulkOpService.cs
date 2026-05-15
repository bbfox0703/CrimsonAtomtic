using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.SaveModel;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Phase identifier for <see cref="ArtifactBulkOpProgress"/> — drives
/// the progress dialog header text so the user can tell scan vs
/// remove apart.
/// </summary>
public enum ArtifactBulkOpPhase
{
    /// <summary>Walking save blocks to find Sealed Abyss Artifact items.</summary>
    Scanning,
    /// <summary>One-shot batch <c>ListRemoveElement</c> across every located artifact.</summary>
    ArtifactRemove,
    /// <summary>All phases finished (success / cancelled / error).</summary>
    Done,
}

/// <summary>
/// One progress tick reported from
/// <see cref="ArtifactBulkOpService.RunAsync"/> back to the modal
/// dialog. Reported on every step transition.
/// </summary>
public readonly record struct ArtifactBulkOpProgress(
    ArtifactBulkOpPhase Phase,
    int Processed,
    int Total,
    string Message);

/// <summary>
/// Final result of the bulk operation. Counts match what was actually
/// applied — on a mid-batch error the partial counts reflect the
/// progress before the failure.
/// </summary>
/// <param name="ArtifactsRemoved">
/// Number of Sealed Abyss Artifact items removed from inventories.
/// </param>
/// <param name="ArtifactItemKeysKnown">
/// Number of distinct iteminfo entries whose stringKey starts with
/// <c>Sealed_Abyss_Artifact</c>. 0 when the iteminfo bridge wasn't
/// loaded (artifact removal is a no-op in that case).
/// </param>
public readonly record struct ArtifactBulkOpResult(
    int ArtifactsRemoved,
    int ArtifactItemKeysKnown);

/// <summary>
/// Drives the "drop every Sealed Abyss Artifact in inventory" sweep
/// against a loaded save. Pulled out of the VM so the progress dialog
/// can drive it directly with a <see cref="CancellationToken"/> and
/// <see cref="IProgress{T}"/>; keeps the heavy logic out of XAML
/// code-behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why no challenge completion?</b> An earlier iteration of this
/// service also wrote <c>MissionStateData._state ← 5</c> +
/// <c>_completedTime</c> across catalog Mastery / Combat / Life /
/// Minigame challenges, with the goal of letting the user mark batch
/// progression. <b>That feature was verified to corrupt save state
/// and has been removed.</b>
/// </para>
/// <para>
/// The recipe (<c>_state=5 + _completedTime</c>) is necessary but not
/// sufficient. The engine cross-references catalog
/// <c>MissionStateData._state=5</c> against a matching
/// <c>ContentsMiscSaveData._alertHistorySaveDataList</c> entry — no
/// alert entry → engine treats the catalog state as inconsistent and
/// <i>hides the challenge card</i> in the in-game UI even though the
/// data shows completion. Tested against slot103 (manual single
/// flip) and slot104 (batch flip): both saves regressed
/// previously-visible challenges into the locked / unknown UI state.
/// Restoring the engine-natural recipe requires inserting properly-
/// shaped AlertHistorySaveData entries (and probably a paired
/// negative-keyed mission twin), which is a substantial follow-up
/// not yet shipped.
/// </para>
/// <para>
/// Until that lands, the bulk path covers only the artifact removal —
/// which is clean, harmless, and useful by itself for users who want
/// to clear stuck Sealed Abyss Artifact items the engine can't
/// reconcile (the original motivation for the bulk action).
/// </para>
/// </remarks>
public static class ArtifactBulkOpService
{
    /// <summary>
    /// Class name of every top-level inventory block. Each decodes
    /// into a tree of <c>_inventorylist[N]._itemList[M]</c>.
    /// </summary>
    private const string InventorySaveDataClass = "InventorySaveData";

    /// <summary>
    /// String-key prefix for every Sealed Abyss Artifact iteminfo
    /// entry. The slot103 sample observed 12 distinct ItemKeys all
    /// sharing this prefix
    /// (<c>Sealed_Abyss_Artifact_0006</c>..<c>_0140</c>).
    /// </summary>
    private const string ArtifactItemKeyPrefix = "Sealed_Abyss_Artifact";

    /// <summary>
    /// Drive the whole sweep. Reports phase transitions through
    /// <paramref name="progress"/>; honours
    /// <paramref name="cancellationToken"/> between phases (the FFI
    /// batch call itself isn't cancellable mid-flight — Rust holds
    /// the body lock for the duration).
    /// </summary>
    public static async Task<ArtifactBulkOpResult> RunAsync(
        ISaveLoader loader,
        LocalizationProvider localization,
        string savePath,
        IReadOnlyList<BlockSummary> blocks,
        IProgress<ArtifactBulkOpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentNullException.ThrowIfNull(blocks);

        return await Task.Run(() =>
        {
            var artifactItemKeys = new HashSet<uint>(
                localization.EnumerateItemsByStringKeyPrefix(ArtifactItemKeyPrefix)
                            .Select(p => p.ItemKey));

            // ── Phase: Scanning ─────────────────────────────────────
            progress?.Report(new ArtifactBulkOpProgress(
                ArtifactBulkOpPhase.Scanning, 0, blocks.Count,
                $"Scanning inventories for Sealed Abyss Artifacts "
                + $"({artifactItemKeys.Count:N0} known artifact item key(s))…"));

            var removes = new List<RemoveTarget>();
            for (var bi = 0; bi < blocks.Count; bi++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var b = blocks[bi];
                if (bi % 50 == 0)
                {
                    progress?.Report(new ArtifactBulkOpProgress(
                        ArtifactBulkOpPhase.Scanning, bi, blocks.Count,
                        $"Scanning blocks: {bi:N0}/{blocks.Count:N0}"));
                }
                if (!string.Equals(b.ClassName, InventorySaveDataClass, StringComparison.Ordinal)
                    || artifactItemKeys.Count == 0)
                {
                    continue;
                }
                BlockDetails top;
                try
                {
                    top = loader.LoadBlockDetails(savePath, b.Index);
                }
                catch (CrimsonSaveException)
                {
                    continue;
                }
                CollectArtifactRemoves(top, b.Index, artifactItemKeys, removes);
            }

            // Sort artifact removes so an earlier remove never shifts
            // the index of a later one targeting the same bag's
            // _itemList. Within a single list, descending element
            // index is sufficient.
            var sortedRemoves = removes
                .OrderBy(r => r.BlockIndex)
                .ThenBy(r => r.BagIndex)
                .ThenByDescending(r => r.ElementIndex)
                .ToList();

            // ── Phase: ArtifactRemove ───────────────────────────────
            // One FFI batch call across every drop. Pre-sorted so
            // earlier removes don't shift later indexes.
            var removedApplied = 0;
            if (sortedRemoves.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ArtifactBulkOpProgress(
                    ArtifactBulkOpPhase.ArtifactRemove, 0, sortedRemoves.Count,
                    $"Removing artifacts via batch: {sortedRemoves.Count:N0} op(s)…"));
                var removeOps = new List<ListRemoveBatchOp>(sortedRemoves.Count);
                foreach (var r in sortedRemoves)
                {
                    removeOps.Add(new ListRemoveBatchOp(
                        r.BlockIndex, r.BagPath, r.ListFieldIndex, r.ElementIndex));
                }
                loader.ListRemoveElementsBatch(removeOps);
                removedApplied = sortedRemoves.Count;
                progress?.Report(new ArtifactBulkOpProgress(
                    ArtifactBulkOpPhase.ArtifactRemove, removedApplied, sortedRemoves.Count,
                    $"Artifact remove batch: {removedApplied:N0}/{sortedRemoves.Count:N0}"));
            }

            progress?.Report(new ArtifactBulkOpProgress(
                ArtifactBulkOpPhase.Done,
                removedApplied,
                sortedRemoves.Count,
                "Done."));

            return new ArtifactBulkOpResult(
                ArtifactsRemoved: removedApplied,
                ArtifactItemKeysKnown: artifactItemKeys.Count);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Walk an <c>InventorySaveData</c> top block, append a
    /// <see cref="RemoveTarget"/> for every <c>ItemSaveData</c> whose
    /// <c>_itemKey</c> sits in <paramref name="artifactItemKeys"/>.
    /// </summary>
    private static void CollectArtifactRemoves(
        BlockDetails top,
        int blockIndex,
        HashSet<uint> artifactItemKeys,
        List<RemoveTarget> removes)
    {
        for (var f = 0; f < top.Fields.Count; f++)
        {
            var invList = top.Fields[f];
            if (!string.Equals(invList.Name, "_inventorylist", StringComparison.Ordinal)
                || invList.Elements is not { Count: > 0 } bags)
            {
                continue;
            }
            for (var bagIdx = 0; bagIdx < bags.Count; bagIdx++)
            {
                var bag = bags[bagIdx];
                for (var g = 0; g < bag.Fields.Count; g++)
                {
                    var itemListField = bag.Fields[g];
                    if (!string.Equals(itemListField.Name, "_itemList", StringComparison.Ordinal)
                        || itemListField.Elements is not { Count: > 0 } items)
                    {
                        continue;
                    }
                    for (var itemIdx = 0; itemIdx < items.Count; itemIdx++)
                    {
                        var item = items[itemIdx];
                        DecodedFieldRow? ikFld = null;
                        foreach (var inner in item.Fields)
                        {
                            if (inner.Name == "_itemKey" && inner.Present)
                            {
                                ikFld = inner;
                                break;
                            }
                        }
                        if (ikFld is null
                            || !TryParseScalarUInt(ikFld.Value, out var ikVal)
                            || ikVal > uint.MaxValue
                            || !artifactItemKeys.Contains((uint)ikVal))
                        {
                            continue;
                        }
                        var bagPath = new[] { new PathStep((uint)f, (uint)bagIdx) };
                        removes.Add(new RemoveTarget(
                            blockIndex, bagPath, g, itemIdx, (uint)ikVal, bagIdx));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Pre-formatted scalar value (<c>"123 &lt;u32&gt;"</c>) →
    /// <see cref="ulong"/>. Returns false on signed / float / bytes
    /// values so the caller can skip the field instead of writing a
    /// wrong number.
    /// </summary>
    private static bool TryParseScalarUInt(string formatted, out ulong value)
    {
        value = 0;
        if (!ScalarFieldEditing.TryParse(formatted, out var rawText, out var tag))
        {
            return false;
        }
        if (tag is not ("u8" or "u16" or "u32" or "u64"))
        {
            return false;
        }
        return ulong.TryParse(rawText, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out value);
    }

    /// <summary>
    /// One artifact element flagged for removal. Carries the
    /// <see cref="BagIndex"/> separately from <see cref="BagPath"/>
    /// so the post-collection sort can group "removes within the
    /// same bag" without re-parsing the path.
    /// </summary>
    private readonly record struct RemoveTarget(
        int BlockIndex,
        PathStep[] BagPath,
        int ListFieldIndex,
        int ElementIndex,
        uint ItemKey,
        int BagIndex);
}
