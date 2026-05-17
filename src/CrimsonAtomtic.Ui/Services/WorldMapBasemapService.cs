using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Extracts + caches the world-map basemap PNG used by the World Map
/// window. Same on-demand extraction pattern as
/// <see cref="IconExtractionService"/>: pulls a DDS from the user's own
/// game install (no asset shipped with the editor), decodes it, and
/// writes a full-resolution PNG to <see cref="CacheDirectory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The basemap is a parchment-style composite produced by
/// <see cref="WorldMapCompositor"/> from three layers (all under
/// <c>0012/</c>):
/// </para>
/// <list type="bullet">
///   <item><c>ui/texture/image/worldmap/cd_worldmap_blur_height.dds</c> — 8192² grayscale relief / land-water mask</item>
///   <item><c>ui/texture/cd_worldmap_paper_pattern.dds</c> — small tileable parchment texture (512² BC1)</item>
///   <item><c>ui/texture/image/worldmap/cd_worldmap_road_sdf_32768x32768.dds</c> — 8192² road SDF</item>
/// </list>
/// <para>
/// The composite output matches the visual style of the web-fetched
/// <c>crimson-desert-full-world-map.jpg</c>: parchment land, muted-
/// teal water, road network. Final resolution is 4096×4096 (~12 MB
/// PNG on disk).
/// </para>
/// <para>
/// Cache layout (under <see cref="CacheDirectory"/>):
/// <code>
///   parchment_basemap.png    — 4096×4096 PNG, RGBA8888
/// </code>
/// Subsequent calls skip the extract + composite step when the PNG is
/// already on disk. First call costs ~5-10 seconds + ~400 MB transient
/// memory (8192² source layers decode to RGBA). <see cref="EnsureBasemapAsync"/>
/// takes a <c>forceRefresh</c> flag for the rare case where a game
/// patch changes the layers and we need to invalidate the cache.
/// </para>
/// </remarks>
public static class WorldMapBasemapService
{
    private const string BlurHeightDirInPaz = "ui/texture/image/worldmap";
    private const string BlurHeightFileInPaz = "cd_worldmap_blur_height.dds";
    private const string PaperPatternDirInPaz = "ui/texture";
    private const string PaperPatternFileInPaz = "cd_worldmap_paper_pattern.dds";
    private const string RoadSdfDirInPaz = "ui/texture/image/worldmap";
    private const string RoadSdfFileInPaz = "cd_worldmap_road_sdf_32768x32768.dds";
    private const string BasemapCachedPngName = "parchment_basemap.png";
    private const string GameGroup = "0012";

    /// <summary>
    /// Per-user cache directory. Sits under <c>%LOCALAPPDATA%</c> on
    /// Windows, mirroring <see cref="IconExtractionService"/>'s default.
    /// </summary>
    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrimsonAtomtic",
        "WorldMap");

    /// <summary>
    /// Full path to the cached basemap PNG.
    /// </summary>
    public static string CachedBasemapPath => Path.Combine(
        CacheDirectory, BasemapCachedPngName);

    /// <summary>
    /// Extract + decode + cache the basemap PNG if it isn't already on
    /// disk. Returns the path to the cached PNG.
    /// </summary>
    /// <param name="paz">
    /// PAZ extractor for pulling the DDS out of <c>0015/0.paz</c>.
    /// </param>
    /// <param name="gameRoot">
    /// Crimson Desert install root (the directory containing
    /// <c>0008/</c>, <c>0012/</c>, <c>0015/</c>, etc.).
    /// </param>
    /// <param name="forceRefresh">
    /// When <c>true</c>, re-extracts even if the PNG is on disk. Useful
    /// after a game patch.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">
    /// <paramref name="gameRoot"/> doesn't have the <c>0015/0.pamt</c>
    /// expected by the basemap path.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The extracted DDS isn't in a recognised format. Indicates a game
    /// patch added a new BC format we don't yet decode.
    /// </exception>
    public static async Task<string> EnsureBasemapAsync(
        IPazExtractor paz,
        string gameRoot,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paz);
        ArgumentException.ThrowIfNullOrEmpty(gameRoot);

        Directory.CreateDirectory(CacheDirectory);
        var pngPath = CachedBasemapPath;
        if (!forceRefresh && File.Exists(pngPath))
        {
            return pngPath;
        }

        var pamtPath = Path.Combine(gameRoot, GameGroup, "0.pamt");
        if (!File.Exists(pamtPath))
        {
            throw new FileNotFoundException(
                $"Game install missing group {GameGroup}'s PAMT manifest.", pamtPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Each layer extract is a synchronous FFI call; hop the whole
        // bundle to a worker thread so the UI dispatcher doesn't block
        // on the ~5-10 s composite step.
        var png = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blurHeight = paz.ExtractFile(pamtPath,
                BlurHeightDirInPaz, BlurHeightFileInPaz);
            cancellationToken.ThrowIfCancellationRequested();
            var paperPattern = paz.ExtractFile(pamtPath,
                PaperPatternDirInPaz, PaperPatternFileInPaz);
            cancellationToken.ThrowIfCancellationRequested();
            var roadSdf = paz.ExtractFile(pamtPath,
                RoadSdfDirInPaz, RoadSdfFileInPaz);
            cancellationToken.ThrowIfCancellationRequested();
            return WorldMapCompositor.CompositeParchment(
                blurHeight, paperPattern, roadSdf);
        }, cancellationToken).ConfigureAwait(false);

        await File.WriteAllBytesAsync(pngPath, png, cancellationToken).ConfigureAwait(false);
        return pngPath;
    }

    /// <summary>
    /// Returns <c>true</c> when the basemap PNG is already on disk. Used
    /// by the View Model to decide whether the "extracting…" status
    /// banner needs to be shown.
    /// </summary>
    public static bool IsBasemapCached() => File.Exists(CachedBasemapPath);
}
