using Avalonia.Media.Imaging;
using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Resolves NPC portrait images by <c>CharacterKey</c>. On-demand
/// pipeline:
///
/// <list type="number">
///   <item>list every NPC-portrait DDS path in <c>0012/0.pamt</c> via
///     <see cref="IPazExtractor.ListNpcPortraits"/> (cached for the
///     provider lifetime);</item>
///   <item>per CharacterKey, score the loaded display + internal names
///     against the portrait list via
///     <see cref="LocalizationProvider.ResolvePortraitForCharacter"/>;</item>
///   <item>PAZ-extract the winning DDS;</item>
///   <item>DDS → resized WebP via <see cref="IconImageEncoder"/>;</item>
///   <item>write <c>&lt;cacheRoot&gt;/&lt;CharacterKey&gt;.webp</c>;</item>
///   <item>decode to <see cref="Bitmap"/> and cache per key.</item>
/// </list>
///
/// <para>
/// Mirrors <see cref="IconProvider"/>'s lazy-load + Bitmap-cache shape
/// but adds the extraction half on the cold path — portraits aren't
/// pre-extracted via a Tools menu action (the mercenary list is small
/// enough — ~100 entries — that on-demand is acceptable, and the
/// portrait match has to know which CharacterKeys the user actually
/// holds before it can be batched).
/// </para>
///
/// <para>
/// Misses are cached as <c>null</c> so the dialog doesn't hammer the
/// FFI for a row whose portrait genuinely doesn't exist. Mid-confidence
/// matches (score below <see cref="MinAcceptableScore"/>) are also
/// rejected — Pearl Abyss's name-to-portrait association isn't 1:1 and
/// a low score usually means "the closest portrait, but probably wrong".
/// </para>
/// </summary>
public sealed class PortraitProvider
{
    /// <summary>
    /// Subdirectory under <c>%LOCALAPPDATA%\CrimsonAtomtic\</c> where
    /// extracted portraits live. Mirrors
    /// <see cref="IconProvider.SubdirectoryName"/> — both pieces of the
    /// editor's persistent image cache live as sibling folders under
    /// one root.
    /// </summary>
    public const string SubdirectoryName = "PortraitCache";

    /// <summary>
    /// Score threshold below which a portrait match is rejected as
    /// noise. Tuned from the upstream Rust bridge's calibration:
    /// 0 = no match, ~30 = noise floor, ~50 = suggestive, >70 = exact
    /// normalised match. We set 50 to skip rows where the matcher
    /// returns a "best of bad options" portrait.
    /// </summary>
    public const int MinAcceptableScore = 50;

    /// <summary>
    /// PAZ group path containing NPC portrait DDS files (the same
    /// group that ships <c>ui/texture/icon/</c>). 0012 is canonical
    /// across 1.05 / 1.06 / 1.07.
    /// </summary>
    private const string PortraitsGroupRelative = "0012";

    /// <summary>
    /// Target dimensions for the resized portrait. Square 128 is a
    /// reasonable middle ground between the DataGrid row height and
    /// portrait detail; the source DDS is usually 256+px so we
    /// downscale.
    /// </summary>
    private const int TargetSize = 128;

    /// <summary>WebP quality (0–100). Matches <c>IconExtractionService</c>'s 80.</summary>
    private const int WebpQuality = 80;

    private readonly string _root;
    private readonly IPazExtractor _paz;
    private readonly LocalizationProvider _localization;
    private readonly string? _pamtPath;
    private readonly Dictionary<uint, Bitmap?> _cache = new();
    private readonly object _gate = new();
    private byte[]? _portraitListBuffer;
    private bool _portraitListLoadAttempted;

    /// <summary>True iff the provider's prerequisites are satisfied.</summary>
    public bool IsAvailable { get; }

    /// <summary>Cache root directory (always set; created on construction).</summary>
    public string Root => _root;

    /// <summary>
    /// Resolve the canonical portrait-cache directory for a given
    /// platform-paths instance — <c>&lt;LocalAppData&gt;\PortraitCache\</c>.
    /// Pure path math; doesn't touch the filesystem.
    /// </summary>
    public static string ResolveRoot(string localAppDataDirectory) =>
        Path.Combine(localAppDataDirectory, SubdirectoryName);

    public PortraitProvider(
        string cacheRootDirectory,
        IPazExtractor paz,
        LocalizationProvider localization,
        string? gameRoot)
    {
        ArgumentNullException.ThrowIfNull(cacheRootDirectory);
        ArgumentNullException.ThrowIfNull(paz);
        ArgumentNullException.ThrowIfNull(localization);
        _root = cacheRootDirectory;
        _paz = paz;
        _localization = localization;
        if (string.IsNullOrWhiteSpace(cacheRootDirectory))
        {
            // Bootstrap stub.
            IsAvailable = false;
            return;
        }
        try
        {
            Directory.CreateDirectory(_root);
        }
        catch (IOException)
        {
            IsAvailable = false;
            return;
        }
        catch (UnauthorizedAccessException)
        {
            IsAvailable = false;
            return;
        }
        // Cache the 0012 PAMT path for the on-demand pipeline. Note we
        // don't fail availability when gameRoot is null — the provider
        // serves disk-cached portraits even with no live install, since
        // the cache survives across sessions and across game-root
        // changes.
        if (!string.IsNullOrEmpty(gameRoot))
        {
            var pamt = Path.Combine(gameRoot, PortraitsGroupRelative, "0.pamt");
            if (File.Exists(pamt))
            {
                _pamtPath = pamt;
            }
        }
        IsAvailable = Directory.Exists(_root);
    }

    /// <summary>
    /// Lookup the portrait for <paramref name="characterKey"/>. Returns
    /// <c>null</c> when:
    /// <list type="bullet">
    ///   <item>the provider isn't available;</item>
    ///   <item>the key isn't covered by characterinfo.pabgb;</item>
    ///   <item>no portrait matched above
    ///     <see cref="MinAcceptableScore"/>;</item>
    ///   <item>the DDS extracted but failed to decode.</item>
    /// </list>
    /// Synchronous; cached. Same key returns the same Bitmap instance
    /// across calls.
    /// </summary>
    public Bitmap? GetPortrait(uint characterKey)
    {
        if (!IsAvailable || characterKey == 0)
        {
            return null;
        }
        lock (_gate)
        {
            if (_cache.TryGetValue(characterKey, out var cached))
            {
                return cached;
            }
        }

        Bitmap? bmp = ResolveAndDecode(characterKey);

        lock (_gate)
        {
            // Race-safe cache insertion — keep the first winner so a
            // bound Bitmap doesn't get disposed under a UI element.
            if (_cache.TryGetValue(characterKey, out var winner))
            {
                bmp?.Dispose();
                return winner;
            }
            _cache[characterKey] = bmp;
        }
        return bmp;
    }

    private Bitmap? ResolveAndDecode(uint characterKey)
    {
        // Disk-cache hit: read existing WebP, skip the FFI / DDS path
        // entirely. Survives across sessions even if the game install
        // is unmounted.
        var cachedFile = Path.Combine(_root, $"{characterKey}.webp");
        if (File.Exists(cachedFile))
        {
            return TryDecodeBitmap(cachedFile);
        }

        // Cold path requires the live game install (we extract the DDS
        // from 0012/0.paz on demand).
        if (_pamtPath is null)
        {
            return null;
        }

        var portraitList = EnsurePortraitList();
        if (portraitList is null || portraitList.Length == 0)
        {
            return null;
        }

        var match = _localization.ResolvePortraitForCharacter(characterKey, portraitList);
        if (match is not { } m || m.Score < MinAcceptableScore)
        {
            return null;
        }

        // The matcher returns "<dir>/<filename>". Split on the last slash —
        // PAZ extraction takes them as separate args.
        var slash = m.Path.LastIndexOf('/');
        if (slash <= 0 || slash >= m.Path.Length - 1)
        {
            return null;
        }
        var dir = m.Path[..slash];
        var file = m.Path[(slash + 1)..];

        byte[] ddsBytes;
        try
        {
            ddsBytes = _paz.ExtractFile(_pamtPath, dir, file);
        }
        catch (CrimsonSaveException) { return null; }
        catch (IOException) { return null; }

        byte[] webp;
        try
        {
            webp = IconImageEncoder.EncodeDdsToWebp(ddsBytes, TargetSize, WebpQuality);
        }
        catch (InvalidDataException) { return null; }
        catch (InvalidOperationException) { return null; }

        try
        {
            File.WriteAllBytes(cachedFile, webp);
        }
        catch (IOException) { /* cache write failed; we can still decode in-memory */ }
        catch (UnauthorizedAccessException) { /* same */ }

        return TryDecodeBitmapFromBytes(webp);
    }

    private byte[]? EnsurePortraitList()
    {
        lock (_gate)
        {
            if (_portraitListLoadAttempted)
            {
                return _portraitListBuffer;
            }
            _portraitListLoadAttempted = true;
        }
        if (_pamtPath is null)
        {
            return null;
        }
        try
        {
            var (buf, _) = _paz.ListNpcPortraits(_pamtPath);
            lock (_gate)
            {
                _portraitListBuffer = buf;
                return _portraitListBuffer;
            }
        }
        catch (CrimsonSaveException) { return null; }
        catch (IOException) { return null; }
    }

    private static Bitmap? TryDecodeBitmap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
#pragma warning disable CA1031 // Avalonia/SkiaSharp throws Exception on malformed bytes
        catch (Exception) { return null; }
#pragma warning restore CA1031
    }

    private static Bitmap? TryDecodeBitmapFromBytes(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
#pragma warning disable CA1031
        catch (Exception) { return null; }
#pragma warning restore CA1031
    }
}
