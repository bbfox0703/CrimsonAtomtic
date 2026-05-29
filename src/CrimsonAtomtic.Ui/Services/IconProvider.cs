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
/// them with CrimsonAtomtic itself. The icons live in a fixed
/// directory under <c>%LOCALAPPDATA%\CrimsonAtomtic\</c>
/// (see <see cref="SubdirectoryName"/>), populated by
/// Tools → Extract Icons from Game Data. No user-configurable path —
/// %LOCALAPPDATA% is per-user, per-machine, and matches where
/// SaveBackupService writes, so the editor's persistent data lives
/// in one predictable place.
/// </para>
///
/// <para>
/// File format: WebP (CRIMSON-DESERT-SAVE-EDITOR's <c>icons_local/</c>
/// folder uses it; SkiaSharp / Avalonia 12 decodes it natively).
/// Naming convention: filename is the decimal <c>ItemKey</c>, e.g.
/// <c>11.webp</c> for Camp Funds.
/// </para>
/// </summary>
public sealed class IconProvider
{
    /// <summary>
    /// Subdirectory under <c>%LOCALAPPDATA%\CrimsonAtomtic\</c>
    /// where extracted icons live. Mirrors <see cref="SaveBackupService.BackupsSubdirectory"/>
    /// — both pieces of the editor's persistent data live as
    /// sibling folders under one root.
    /// </summary>
    public const string SubdirectoryName = "IconCache";

    private readonly string _root;
    private readonly Dictionary<uint, Bitmap?> _cache = new();
    private readonly object _gate = new();
    private int _hits;
    private int _misses;
    private int _decodeFailures;
    private string? _lastError;

    /// <summary>
    /// True once the root directory exists on disk. Failures during
    /// the create attempt (rare — read-only filesystem, AV interference)
    /// flip this to false and the provider degrades to "no icons
    /// available" rather than crashing the UI.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>The active icon root (always set; created on first use).</summary>
    public string Root => _root;

    /// <summary>
    /// Count of <c>.webp</c> files in the root, measured once at
    /// construction. <c>0</c> when the directory is empty (e.g. fresh
    /// install before Tools → Extract Icons has run).
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

    /// <summary>
    /// Resolve the canonical icon-cache directory for a given platform
    /// paths instance — <c>&lt;LocalAppData&gt;\IconCache\</c>. Pure
    /// path math; doesn't touch the filesystem.
    /// </summary>
    public static string ResolveRoot(string localAppDataDirectory) =>
        Path.Combine(localAppDataDirectory, SubdirectoryName);

    public IconProvider(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        _root = rootDirectory;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            // Bootstrap stub — used before platform paths are known.
            // App.axaml.cs replaces this via ConfigureIconProvider
            // immediately after constructing LocalizationProvider.
            IsAvailable = false;
            return;
        }
        // Eagerly create the dir so Tools → Extract Icons has a
        // landing pad without a separate first-run codepath. Swallow
        // failure modes that aren't actionable (no permission to
        // write under LocalAppData) — the provider just reports zero
        // files and IsAvailable=false.
        try
        {
            Directory.CreateDirectory(_root);
            IsAvailable = Directory.Exists(_root);
        }
        catch (IOException)
        {
            IsAvailable = false;
        }
        catch (UnauthorizedAccessException)
        {
            IsAvailable = false;
        }
        if (IsAvailable)
        {
            try
            {
                FileCount = Directory.EnumerateFiles(_root, "*.webp",
                                                      SearchOption.TopDirectoryOnly).Count();
            }
            catch (IOException) { /* leave 0 */ }
            catch (UnauthorizedAccessException) { /* leave 0 */ }
        }
    }

    /// <summary>
    /// Lookup the icon for <paramref name="itemKey"/>. Returns
    /// <c>null</c> when no icon directory is configured or the file
    /// doesn't exist or fails to decode. Cached — same key returns
    /// the same Bitmap instance across calls.
    /// </summary>
    public Bitmap? GetItemIcon(uint itemKey)
    {
        if (!IsAvailable)
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
