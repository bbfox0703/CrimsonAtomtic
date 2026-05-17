using System.Collections.Generic;
using System.Globalization;
using Avalonia;

namespace CrimsonAtomtic.Ui.Services;

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

    public UiLanguageService(Application app)
    {
        _app = app;
        Current = DefaultCode;
    }

    /// <summary>Currently-applied language code. Defaults to <see cref="DefaultCode"/> until <see cref="Apply"/> is called.</summary>
    public string Current { get; private set; }

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
            return;
        }

        var merged = _app.Resources.MergedDictionaries;

        // Find the ResourceInclude whose Source ends in /<code>.axaml.
        // The avares:// URIs declared in App.axaml carry that path
        // segment verbatim — we match on it rather than on full URI
        // equality so an asset-URI host / scheme tweak in a future
        // Avalonia rev doesn't silently break the swap.
        var needle = $"/Resources/Strings/{languageCode}.axaml";
        Avalonia.Markup.Xaml.Styling.ResourceInclude? target = null;
        int targetIdx = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i] is Avalonia.Markup.Xaml.Styling.ResourceInclude ri &&
                ri.Source is { } src &&
                src.AbsolutePath.EndsWith(needle, System.StringComparison.OrdinalIgnoreCase))
            {
                target = ri;
                targetIdx = i;
                break;
            }
        }

        // Defensive: language dictionary not registered in App.axaml —
        // skip silently rather than crashing. Current stays at its
        // previous value so the UI keeps rendering in the prior language.
        if (target is null)
        {
            return;
        }

        // No-op when the target is already last (the active position).
        if (targetIdx == merged.Count - 1)
        {
            Current = languageCode;
            return;
        }

        // Move-to-end. RemoveAt + Add fires the collection-changed
        // notification Avalonia listens on for DynamicResource refresh.
        merged.RemoveAt(targetIdx);
        merged.Add(target);

        Current = languageCode;
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
