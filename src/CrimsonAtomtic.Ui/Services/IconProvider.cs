using Avalonia.Media.Imaging;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Resolves item icons by <c>ItemKey</c>. Looks for
/// <c>&lt;root&gt;/&lt;itemKey&gt;.webp</c> on disk and caches the
/// decoded <see cref="Bitmap"/> per key. Misses are also cached
/// (as <c>null</c>) so re-renders of a missing-icon row don't hit
/// the filesystem every layout pass.
///
/// <para>
/// Pearl Abyss owns the icon artwork — we deliberately don't bundle
/// them with CrimsonAtomtic itself. The user points at their own
/// extracted folder via
/// <c>AppSettings.IconCacheDirectory</c> (Tools menu → Set icon
/// folder…) or drops the icons into a default <c>IconCache/</c>
/// directory next to the exe.
/// </para>
///
/// <para>
/// File format: WebP (the reference repo's <c>icons_local/</c>
/// folder uses it; SkiaSharp / Avalonia 12 decodes it natively).
/// Naming convention: filename is the decimal <c>ItemKey</c>, e.g.
/// <c>11.webp</c> for Camp Funds.
/// </para>
/// </summary>
public sealed class IconProvider
{
    private readonly string? _root;
    private readonly Dictionary<uint, Bitmap?> _cache = new();
    private readonly object _gate = new();
    private int _hits;
    private int _misses;
    private int _decodeFailures;
    private string? _lastError;

    /// <summary>True when a usable icon directory was found at startup.</summary>
    public bool IsAvailable => _root is not null;

    /// <summary>The active icon root, or <c>null</c> when no directory was found.</summary>
    public string? Root => _root;

    /// <summary>
    /// Count of <c>.webp</c> files in the configured root, measured
    /// once at construction. <c>0</c> when no root is configured.
    /// Drives the "Icons: N files" status indicator so the user can
    /// tell at a glance whether the path actually contains anything.
    /// </summary>
    public int FileCount { get; }

    /// <summary>Successful Bitmap loads so far this session.</summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>Lookups where the file didn't exist (no error, just absent).</summary>
    public int Misses => Volatile.Read(ref _misses);

    /// <summary>Files that existed but failed to decode (corruption, codec issue, etc.).</summary>
    public int DecodeFailures => Volatile.Read(ref _decodeFailures);

    /// <summary>
    /// Most recent decode error message, or <c>null</c> when no
    /// failure has occurred. Surfaced in the status bar so the user
    /// can see *why* icons are missing instead of just "they didn't
    /// show up".
    /// </summary>
    public string? LastError => _lastError;

    public IconProvider(string? configuredPath, string? exeDirectory)
    {
        // Probe order: explicit setting wins, then a sibling folder
        // next to the exe (handy for "drop a copy here" workflows),
        // then nothing. We deliberately don't probe %LOCALAPPDATA% —
        // icons are big enough that the user should make an explicit
        // choice about where they live.
        string?[] candidates =
        [
            configuredPath,
            exeDirectory is null ? null : Path.Combine(exeDirectory, "IconCache"),
        ];
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                _root = candidate;
                try
                {
                    FileCount = Directory.EnumerateFiles(candidate, "*.webp",
                                                          SearchOption.TopDirectoryOnly).Count();
                }
                catch (IOException) { /* leave 0 */ }
                catch (UnauthorizedAccessException) { /* leave 0 */ }
                return;
            }
        }
        _root = null;
    }

    /// <summary>
    /// Lookup the icon for <paramref name="itemKey"/>. Returns
    /// <c>null</c> when no icon directory is configured or the file
    /// doesn't exist or fails to decode. Cached — same key returns
    /// the same Bitmap instance across calls.
    /// </summary>
    public Bitmap? GetItemIcon(uint itemKey)
    {
        if (_root is null)
        {
            return null;
        }
        lock (_gate)
        {
            if (_cache.TryGetValue(itemKey, out var cached))
            {
                return cached;
            }
        }
        // Read + decode outside the lock — Bitmap construction is
        // potentially slow on a cold cache, and we don't want it
        // blocking other lookups for unrelated keys.
        Bitmap? bmp = null;
        var path = Path.Combine(_root, $"{itemKey}.webp");
        if (!File.Exists(path))
        {
            Interlocked.Increment(ref _misses);
        }
        else
        {
            try
            {
                using var stream = File.OpenRead(path);
                bmp = new Bitmap(stream);
                Interlocked.Increment(ref _hits);
            }
            catch (IOException ex)
            {
                _lastError = $"IO: {ex.Message}";
                Interlocked.Increment(ref _decodeFailures);
            }
            catch (UnauthorizedAccessException ex)
            {
                _lastError = $"Access: {ex.Message}";
                Interlocked.Increment(ref _decodeFailures);
            }
            // Avalonia / SkiaSharp throws Exception on malformed image
            // bytes — swallow broadly to keep one bad file from
            // breaking the whole grid render, but record the message
            // so the status bar can show "decode failed: ..." instead
            // of empty cells with no explanation.
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _lastError = $"Decode: {ex.GetType().Name}: {ex.Message}";
                Interlocked.Increment(ref _decodeFailures);
            }
#pragma warning restore CA1031
        }
        lock (_gate)
        {
            // Someone else may have raced us — keep the first result
            // to avoid disposing a Bitmap that's already bound.
            if (_cache.TryGetValue(itemKey, out var winner))
            {
                bmp?.Dispose();
                return winner;
            }
            _cache[itemKey] = bmp;
        }
        return bmp;
    }
}
