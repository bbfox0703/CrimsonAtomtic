using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Orchestrates the end-to-end icon-extraction pipeline:
///
/// <list type="number">
///   <item>walk every <c>ItemKey</c> in the loaded iteminfo;</item>
///   <item>look up the item's <c>icon_path</c> StringInfoKey hash;</item>
///   <item>resolve the hash through the stringinfo bridge to a
///         texture name (e.g. <c>"ItemIcon_Prefab_cd_phm_04_arw_0020"</c>);</item>
///   <item>derive the on-disk DDS filename (lowercase the name +
///         <c>.dds</c>);</item>
///   <item>PAZ-extract it from <c>0012/ui/texture/icon/</c>;</item>
///   <item>decode + resize + WebP-encode via <see cref="IconImageEncoder"/>;</item>
///   <item>write <c>&lt;cache&gt;/&lt;ItemKey&gt;.webp</c>.</item>
/// </list>
///
/// Runs sequentially on whichever thread the caller invokes it on —
/// callers schedule it onto a background thread via <c>Task.Run</c>
/// and observe progress through <see cref="IProgress{T}"/>. Honours
/// <see cref="CancellationToken"/> between items.
///
/// Per-item failures (missing icon hash, unresolved name, DDS not in
/// the archive, decoder rejection) are counted in
/// <see cref="IconExtractionResult"/> and the first handful of
/// messages are kept for the post-run summary. A single bad item
/// never aborts the run.
/// </summary>
public static class IconExtractionService
{
    private const string IconDirectoryInPaz = "ui/texture/icon";
    private const int IconTargetSize = 64;
    private const int WebpQuality = 80;
    private const int MaxFailureSamples = 10;
    private const int ProgressEveryN = 25;

    /// <summary>
    /// Run the full extraction pass.
    /// </summary>
    /// <param name="localization">
    /// Pre-bootstrapped <see cref="LocalizationProvider"/>. Must have
    /// <c>HasStringInfo</c> = true and <c>ItemCount</c> > 0; the
    /// caller is responsible for gating the action UI on those
    /// conditions.
    /// </param>
    /// <param name="paz">
    /// PAZ extractor for pulling DDS bytes out of <c>0012/0.paz</c>.
    /// </param>
    /// <param name="gameRoot">
    /// Crimson Desert install root (the directory containing
    /// <c>0008/</c>, <c>0012/</c>, etc.). The <c>0012/0.pamt</c>
    /// manifest under this root is the source for icon DDS.
    /// </param>
    /// <param name="cacheDirectory">
    /// Where to write <c>&lt;ItemKey&gt;.webp</c> files. Created if
    /// it doesn't exist.
    /// </param>
    /// <param name="overwriteExisting">
    /// When <c>false</c> (default), skips items whose <c>.webp</c>
    /// already exists in the cache. Useful for incremental top-ups
    /// after a game patch. When <c>true</c>, re-extracts every item.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink. The service throttles updates to one
    /// per <see cref="ProgressEveryN"/> processed items so a UI
    /// dispatcher isn't drowned in 6,400 cross-thread posts.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation is checked between items and at the start of each
    /// IO call. Already-written files are kept on cancel.
    /// </param>
    public static async Task<IconExtractionResult> RunAsync(
        LocalizationProvider localization,
        IPazExtractor paz,
        string gameRoot,
        string cacheDirectory,
        bool overwriteExisting = false,
        IProgress<IconExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(paz);
        ArgumentException.ThrowIfNullOrEmpty(gameRoot);
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);

        if (!localization.HasStringInfo)
        {
            throw new InvalidOperationException(
                "Stringinfo bridge not loaded; cannot resolve icon paths.");
        }

        var pamtPath = Path.Combine(gameRoot, "0012", "0.pamt");
        if (!File.Exists(pamtPath))
        {
            throw new FileNotFoundException(
                "Game install missing group 0012's PAMT manifest.", pamtPath);
        }

        Directory.CreateDirectory(cacheDirectory);

        var total = localization.ItemCount;
        var stats = new ExtractionStats();
        var failureSamples = new List<string>(MaxFailureSamples);

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOneAsync(
                i, localization, paz, pamtPath, cacheDirectory,
                overwriteExisting, stats, failureSamples, cancellationToken).ConfigureAwait(false);

            if ((i + 1) % ProgressEveryN == 0 || i + 1 == total)
            {
                progress?.Report(new IconExtractionProgress(
                    Processed: i + 1,
                    Total: total,
                    Written: stats.Written,
                    Failed: stats.Failed));
            }
        }

        return new IconExtractionResult(
            Total: total,
            Written: stats.Written,
            SkippedAlreadyCached: stats.SkippedAlreadyCached,
            SkippedNoIcon: stats.SkippedNoIcon,
            SkippedNoString: stats.SkippedNoString,
            SkippedNotInArchive: stats.SkippedNotInArchive,
            Failed: stats.Failed,
            FailureSamples: failureSamples);
    }

    private static async Task ProcessOneAsync(
        int index,
        LocalizationProvider localization,
        IPazExtractor paz,
        string pamtPath,
        string cacheDirectory,
        bool overwriteExisting,
        ExtractionStats stats,
        List<string> failureSamples,
        CancellationToken cancellationToken)
    {
        var entry = localization.GetItem(index);
        if (entry is null)
        {
            // Index out of range shouldn't happen — ItemCount is the
            // upper bound — but the catalog enumerates lazily so it's
            // a possible silent gap. Treat as a no-op.
            return;
        }
        var itemKey = entry.Value.ItemKey;

        var targetPath = Path.Combine(cacheDirectory, $"{itemKey}.webp");
        if (!overwriteExisting && File.Exists(targetPath))
        {
            stats.SkippedAlreadyCached++;
            return;
        }

        var iconHash = localization.GetItemIconPathHash(itemKey);
        if (iconHash is null)
        {
            stats.SkippedNoIcon++;
            return;
        }

        var prefabName = localization.ResolveStringInfoHash(iconHash.Value);
        if (string.IsNullOrEmpty(prefabName))
        {
            stats.SkippedNoString++;
            return;
        }

        var ddsName = prefabName.ToLowerInvariant() + ".dds";
        byte[] ddsBytes;
        try
        {
            ddsBytes = paz.ExtractFile(pamtPath, IconDirectoryInPaz, ddsName);
        }
        catch (CrimsonSaveException ex) when (ex.ErrorCode == NotFoundErrorCode)
        {
            // DDS not in the icon directory — typically a stale prefab
            // reference for a dev / cut item.
            stats.SkippedNotInArchive++;
            return;
        }
        catch (CrimsonSaveException ex)
        {
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): PAZ extract failed ({ex.ErrorCode}) — {ex.Message}");
            return;
        }
        catch (IOException ex)
        {
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): PAZ IO failed — {ex.Message}");
            return;
        }

        byte[] webp;
        try
        {
            webp = IconImageEncoder.EncodeDdsToWebp(ddsBytes, IconTargetSize, WebpQuality);
        }
        catch (InvalidDataException ex)
        {
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): DDS decode failed — {ex.Message}");
            return;
        }
        catch (InvalidOperationException ex)
        {
            // SkiaSharp ScalePixels can fail under low-memory etc.
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): encode failed — {ex.Message}");
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(targetPath, webp, cancellationToken).ConfigureAwait(false);
            stats.Written++;
        }
        catch (IOException ex)
        {
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): write failed — {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            RecordFailure(stats, failureSamples,
                $"itemKey={itemKey} ({prefabName}): write failed — {ex.Message}");
        }
    }

    // Mirror of NativeMethods.NOT_FOUND; kept local so the service
    // doesn't reach into the RustInterop namespace for a single code.
    private const int NotFoundErrorCode = -16;

    private static void RecordFailure(
        ExtractionStats stats, List<string> samples, string message)
    {
        stats.Failed++;
        if (samples.Count < MaxFailureSamples)
        {
            samples.Add(message);
        }
    }

    private sealed class ExtractionStats
    {
        public int Written;
        public int SkippedAlreadyCached;
        public int SkippedNoIcon;
        public int SkippedNoString;
        public int SkippedNotInArchive;
        public int Failed;
    }
}

/// <summary>
/// Live status of an in-flight extraction run. Reported via
/// <see cref="IProgress{T}"/> at roughly every 25 items.
/// </summary>
public sealed record IconExtractionProgress(
    int Processed,
    int Total,
    int Written,
    int Failed);

/// <summary>
/// Summary of a completed extraction run.
/// </summary>
public sealed record IconExtractionResult(
    int Total,
    int Written,
    int SkippedAlreadyCached,
    int SkippedNoIcon,
    int SkippedNoString,
    int SkippedNotInArchive,
    int Failed,
    IReadOnlyList<string> FailureSamples);
