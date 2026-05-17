using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Outcome of the most recent <see cref="UiLanguageService.Apply"/>
/// call. Surfaces silent failures the menu code-path can route to the
/// status bar / a dialog.
/// </summary>
public enum ApplyOutcome
{
    /// <summary><see cref="UiLanguageService.Apply"/> hasn't been called yet.</summary>
    NotYetCalled,
    /// <summary>The target dictionary was reordered to the active (last) position.</summary>
    Swapped,
    /// <summary>The target dictionary was already at the active position — no reorder needed.</summary>
    AlreadyActive,
    /// <summary>The supplied language code isn't in <see cref="UiLanguageService.SupportedCodes"/>.</summary>
    UnsupportedCode,
    /// <summary>
    /// No matching <see cref="Avalonia.Markup.Xaml.Styling.ResourceInclude"/>
    /// was found in <c>Application.Resources.MergedDictionaries</c> —
    /// either the App.axaml declaration is missing the language, or the
    /// avares:// URI matching couldn't identify the entry (AOT regression).
    /// </summary>
    DictionaryNotFound,
}

/// <summary>
/// Runtime UI-language switcher. Owns the active language code and the
/// mechanics of swapping the merged ResourceDictionary in
/// <c>App.Resources.MergedDictionaries</c> at runtime. AXAML bindings
/// must use <c>DynamicResource</c> (not <c>StaticResource</c>) so they
/// re-evaluate against the new dictionary; code-behind callers that
/// already use <see cref="Application.FindResource(object)"/> pick up
/// the change automatically (FindResource is re-evaluated on each call).
/// </summary>
public sealed class UiLanguageService
{
    /// <summary>Language code surfaced when auto-detect picks English.</summary>
    public const string CodeEn = "en";

    /// <summary>Language code for Japanese.</summary>
    public const string CodeJa = "ja";

    /// <summary>Language code for Traditional Chinese (Taiwan + zh-Hant family).</summary>
    public const string CodeZhTw = "zh-TW";

    /// <summary>Default language used as the last-resort fallback.</summary>
    public const string DefaultCode = CodeEn;

    /// <summary>Every code we ship a .axaml dictionary for.</summary>
    public static readonly IReadOnlyList<string> SupportedCodes = new[] { CodeEn, CodeJa, CodeZhTw };

    private readonly Application _app;

    /// <summary>
    /// Snapshot of language-dictionary references captured at
    /// construction time, keyed by language code. Once populated,
    /// <see cref="Apply"/> doesn't need to re-parse URIs — it works
    /// purely by indexing this map and rebuilding the MergedDictionaries
    /// collection with a <c>Clear() + Add()</c> pattern (the only
    /// reordering shape Avalonia 11.3.12 propagates as a
    /// <c>ResourcesChanged</c> event the visual tree picks up — verified
    /// against the working AOBMaker pattern).
    /// </summary>
    private readonly Dictionary<string, IResourceProvider> _dictByCode =
        new(System.StringComparer.OrdinalIgnoreCase);

    public UiLanguageService(Application app)
    {
        _app = app;
        Current = DefaultCode;
        SnapshotDictionaries();
    }

    /// <summary>
    /// Walk <c>App.Resources.MergedDictionaries</c> ONCE at construction
    /// time and capture each language dictionary's
    /// <see cref="IResourceProvider"/> by code. After this, every
    /// <see cref="Apply"/> call is positional — no URI re-parsing per
    /// swap, no AOT-fragile <c>AbsolutePath</c> dependency in the hot
    /// path.
    /// </summary>
    private void SnapshotDictionaries()
    {
        var merged = _app.Resources.MergedDictionaries;
        foreach (var item in merged)
        {
            if (item is not Avalonia.Markup.Xaml.Styling.ResourceInclude ri
                || ri.Source is not { } src)
            {
                continue;
            }
            foreach (var code in SupportedCodes)
            {
                if (MatchesUri(src, $"/{code}.axaml", $"/Resources/Strings/{code}.axaml"))
                {
                    _dictByCode[code] = ri;
                    break;
                }
            }
        }
    }

    /// <summary>Currently-applied language code. Defaults to <see cref="DefaultCode"/> until <see cref="Apply"/> is called.</summary>
    public string Current { get; private set; }

    /// <summary>
    /// Diagnostic value capturing the outcome of the most recent
    /// <see cref="Apply"/> call. Surfaces silent failures (e.g. the
    /// target dictionary wasn't found in <c>App.Resources.MergedDictionaries</c>)
    /// so the menu code-path can route them to the status bar / a
    /// dialog instead of failing without a trace.
    /// </summary>
    public ApplyOutcome LastApplyOutcome { get; private set; } = ApplyOutcome.NotYetCalled;

    /// <summary>
    /// Resolves the effective language code for this launch. The user's
    /// explicit pick (<paramref name="settingsUiLanguage"/>) wins when it
    /// names a supported code; otherwise the OS UI culture is folded into
    /// one of the supported codes. Unknown / unsupported settings values
    /// degrade to auto-detect (rather than throwing or locking the app
    /// into an unloadable language).
    /// </summary>
    /// <remarks>
    /// Pure function — no side effects, no dependency on
    /// <see cref="Application.Current"/>. Unit-tested against synthetic
    /// <see cref="CultureInfo"/> values; never reaches the live OS.
    /// </remarks>
    public static string ResolveActive(string? settingsUiLanguage, CultureInfo currentCulture)
    {
        // Explicit user pick takes precedence — only honored when it
        // names one of the shipped resource dictionaries. Anything else
        // (typo, dictionary we no longer ship, legacy code) falls
        // through to auto-detect so the app stays usable.
        if (!string.IsNullOrEmpty(settingsUiLanguage))
        {
            foreach (var supported in SupportedCodes)
            {
                if (string.Equals(supported, settingsUiLanguage, System.StringComparison.OrdinalIgnoreCase))
                {
                    return supported;
                }
            }
        }

        return DetectFromCulture(currentCulture);
    }

    /// <summary>
    /// Folds an arbitrary <see cref="CultureInfo"/> into one of the
    /// supported codes. Public for direct use by the "Auto" menu item
    /// (it surfaces what auto-detect WOULD pick if the user reset their
    /// preference). Falls through to <see cref="DefaultCode"/> for any
    /// culture we don't ship a dictionary for.
    /// </summary>
    public static string DetectFromCulture(CultureInfo currentCulture)
    {
        var name = currentCulture.Name;

        // Japanese: ja, ja-JP, ja-Jp-JP, …
        if (name.StartsWith("ja", System.StringComparison.OrdinalIgnoreCase))
        {
            return CodeJa;
        }

        // Traditional Chinese family: zh-TW (Taiwan), zh-Hant (script tag),
        // zh-HK (Hong Kong), zh-MO (Macao). zh-CN / zh-SG / zh-Hans fall
        // through — we don't ship a Simplified Chinese dictionary today.
        if (name.StartsWith("zh-TW", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-Hant", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-HK", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("zh-MO", System.StringComparison.OrdinalIgnoreCase))
        {
            return CodeZhTw;
        }

        return DefaultCode;
    }

    /// <summary>
    /// Apply <paramref name="languageCode"/> to the running app — reorders
    /// <c>Application.Resources.MergedDictionaries</c> so the language-N
    /// <c>ResourceInclude</c> sits last (Avalonia's lookup walks in reverse,
    /// so last-merged wins for duplicate keys). All three language
    /// dictionaries are pre-declared in App.axaml at XAML-compile time —
    /// that keeps the AOT trimmer happy (constructing a ResourceInclude
    /// from code trips IL2026 because the (Uri) ctor is RequiresUnreferencedCode).
    /// Unknown codes are ignored (no-op, <see cref="Current"/> unchanged) —
    /// callers should validate via <see cref="SupportedCodes"/> first, or
    /// pass the output of <see cref="ResolveActive"/> which only returns
    /// valid codes.
    /// </summary>
    /// <remarks>
    /// Every AXAML resource reference must use <c>DynamicResource</c> to
    /// observe the swap. <c>StaticResource</c> references are baked in
    /// at load time and ignore later changes.
    /// </remarks>
    public void Apply(string languageCode)
    {
        if (!IsSupported(languageCode))
        {
            LastApplyOutcome = ApplyOutcome.UnsupportedCode;
            return;
        }

        if (!_dictByCode.TryGetValue(languageCode, out var active))
        {
            // Snapshot didn't capture this code at construction — either
            // App.axaml is missing the entry or the URI matcher couldn't
            // identify any of the entries (the AOT regression part 8
            // hardened against).
            LastApplyOutcome = ApplyOutcome.DictionaryNotFound;
            return;
        }

        var merged = _app.Resources.MergedDictionaries;

        // Rebuild via Clear() + Add() — the AOBMaker-proven pattern.
        //
        // Why not RemoveAt + Add (the previous broken approach): in
        // Avalonia 11.3.12, ResourceDictionary's MergedDictionaries is
        // an AvaloniaList whose ForEachItem callbacks only manage
        // AddOwner / RemoveOwner on per-item add / remove. An in-place
        // RemoveAt + Add of the SAME item instance does NOT propagate
        // a ResourcesChanged event the visual tree picks up, so
        // DynamicResource bindings never re-evaluate and the UI keeps
        // rendering the pre-swap language. Verified against
        // https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.12/src/Avalonia.Base/Controls/ResourceDictionary.cs
        // and cross-checked against the working AOBMaker pattern at
        // D:\Github\AOBMaker\src\AOBMaker.UI\I18n\Lang.cs
        // (Lang.ApplyLanguageOrder).
        //
        // Clear() + sequential Add() fires the right cascade: every Add
        // re-AddOwner's the dictionary on the host, which re-emits the
        // resource set into the visual tree's lookup chain. The
        // non-active dictionaries land FIRST (lower priority); the
        // active one goes LAST (Avalonia walks MergedDictionaries in
        // reverse for duplicate-key resolution, so last-merged wins).
        merged.Clear();
        foreach (var code in SupportedCodes)
        {
            if (!string.Equals(code, languageCode, System.StringComparison.OrdinalIgnoreCase)
                && _dictByCode.TryGetValue(code, out var other))
            {
                merged.Add(other);
            }
        }
        merged.Add(active);

        Current = languageCode;
        LastApplyOutcome = ApplyOutcome.Swapped;
    }

    /// <summary>
    /// Decides whether a <see cref="System.Uri"/> Source points at the
    /// resource dictionary identified by <paramref name="filenameSegment"/>
    /// (e.g. <c>/en.axaml</c>) or <paramref name="fullPathSegment"/>
    /// (e.g. <c>/Resources/Strings/en.axaml</c>).
    /// </summary>
    /// <remarks>
    /// Pure function, exposed for direct unit-testing — see
    /// <c>UiLanguageServiceTests.MatchesUri_*</c>. Three URI-string
    /// surfaces are consulted in order: <see cref="System.Uri.OriginalString"/>
    /// (always-set verbatim form), <see cref="System.Uri.ToString"/>
    /// (canonical form), and <see cref="System.Uri.AbsolutePath"/>
    /// (parsed path component — empty in AOT-trimmed publishes for
    /// custom schemes like avares://). The first non-empty match wins.
    /// </remarks>
    public static bool MatchesUri(System.Uri src, string filenameSegment, string fullPathSegment)
    {
        return EndsWithAny(src.OriginalString, filenameSegment, fullPathSegment)
            || EndsWithAny(src.ToString(), filenameSegment, fullPathSegment)
            || EndsWithAny(src.AbsolutePath, filenameSegment, fullPathSegment);

        static bool EndsWithAny(string s, string a, string b) =>
            !string.IsNullOrEmpty(s) && (
                s.EndsWith(a, System.StringComparison.OrdinalIgnoreCase) ||
                s.EndsWith(b, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when <paramref name="code"/> matches one of <see cref="SupportedCodes"/> (case-insensitive).</summary>
    public static bool IsSupported(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }
        foreach (var supported in SupportedCodes)
        {
            if (string.Equals(supported, code, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
