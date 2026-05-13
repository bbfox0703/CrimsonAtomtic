using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Application-level service that owns the loaded PALOC catalog and
/// exposes a single <see cref="Lookup"/> entry point. Composition-root
/// wiring (App.axaml.cs) bootstraps it by:
///
/// <list type="number">
///   <item>locating a Crimson Desert install via <c>IPlatformPaths.GameInstallRoot</c>;</item>
///   <item>extracting <c>localizationstring_eng.paloc</c> from
///         <c>&lt;install&gt;/0020/0.pamt</c> via <see cref="IPazExtractor"/>;</item>
///   <item>handing the resulting bytes to <see cref="NativePalocCatalog.LoadFromBytes"/>.</item>
/// </list>
///
/// Degrades cleanly when no install is found: <see cref="IsLoaded"/>
/// stays false, <see cref="Lookup"/> always returns <c>null</c>. The UI
/// is expected to surface this as "no localization available" rather
/// than failing to start.
/// </summary>
public sealed class LocalizationProvider : IDisposable
{
    /// <summary>
    /// Language code used as part of the PALOC filename
    /// (<c>localizationstring_{code}.paloc</c>). Crimson Desert ships
    /// English by default; other languages may need different PAZ
    /// groups so this is exposed for future expansion.
    /// </summary>
    public const string DefaultLanguage = "eng";

    private const string PalocDirectory = "gamedata/stringtable/binary__";
    private const string PamtRelativePath = "0020/0.pamt";

    private readonly IPazExtractor _paz;
    private NativePalocCatalog? _catalog;

    public LocalizationProvider(IPazExtractor paz)
    {
        ArgumentNullException.ThrowIfNull(paz);
        _paz = paz;
    }

    /// <summary>True once a PALOC has been loaded successfully.</summary>
    public bool IsLoaded => _catalog is not null;

    /// <summary>Entry count of the loaded table, or 0 when not loaded.</summary>
    public int EntryCount => _catalog?.EntryCount ?? 0;

    /// <summary>
    /// Try to load the default-language PALOC from the supplied game
    /// install root. Returns <c>true</c> on success. Errors are
    /// swallowed — the localization layer is best-effort, the editor
    /// must still function without it.
    /// </summary>
    public bool TryBootstrapFromGameRoot(string? gameRoot, string language = DefaultLanguage)
    {
        if (string.IsNullOrEmpty(gameRoot))
        {
            return false;
        }
        try
        {
            var pamt = Path.Combine(gameRoot, PamtRelativePath);
            if (!File.Exists(pamt))
            {
                return false;
            }
            var fileName = $"localizationstring_{language}.paloc";
            var bytes = _paz.ExtractFile(pamt, PalocDirectory, fileName);
            var fresh = NativePalocCatalog.LoadFromBytes(bytes);
            // Atomic swap: dispose any previous catalog only after the
            // new one has loaded cleanly.
            var previous = _catalog;
            _catalog = fresh;
            previous?.Dispose();
            return true;
        }
        catch (CrimsonSaveException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve <paramref name="key"/> against the loaded table. Returns
    /// <c>null</c> when no PALOC is loaded or when the key is absent.
    /// </summary>
    public string? Lookup(string? key)
    {
        if (_catalog is null || string.IsNullOrEmpty(key))
        {
            return null;
        }
        return _catalog.Lookup(key);
    }

    /// <summary>Get the (key, value) at <paramref name="index"/>, or null if out of range / not loaded.</summary>
    public (string Key, string Value)? GetEntry(int index) =>
        _catalog?.GetEntry(index);

    public void Dispose()
    {
        _catalog?.Dispose();
        _catalog = null;
    }
}
