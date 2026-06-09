using Avalonia;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Central helper for resolving localized UI strings from the active
/// merged resource dictionary (en / ja / zh-TW) at runtime, with an
/// inline English fallback supplied by the caller.
///
/// <para>
/// AOT-safe: just <see cref="Application.TryGetResource(object, Avalonia.Styling.ThemeVariant?, out object?)"/>
/// + <see cref="string.Format(IFormatProvider, string, object?[])"/> — no
/// reflection. This replaces the per-view-model <c>LookupUiResourceString</c>
/// copies so confirm dialogs, status lines, and journal entries built in
/// code can be localized the same way as <c>{DynamicResource}</c> bindings
/// in XAML.
/// </para>
///
/// <para>
/// The caller always passes the English fallback, so a missing key degrades
/// to readable text rather than blanking. The <c>?? fallback</c> shape inside
/// <see cref="Format"/> also keeps the format argument non-constant, which is
/// what stops CA1863 / CompositeFormat from firing on <see cref="string.Format"/>.
/// </para>
/// </summary>
public static class UiText
{
    /// <summary>
    /// Resolve <paramref name="key"/> from the active language dictionary,
    /// or return <paramref name="fallback"/> when the key is absent (or no
    /// <see cref="Application"/> is running, e.g. in a headless test host).
    /// </summary>
    public static string Get(string key, string fallback)
        => Application.Current?.TryGetResource(key, null, out var v) == true && v is string s
            ? s
            : fallback;

    /// <summary>
    /// Resolve <paramref name="key"/> (English <paramref name="fallback"/>
    /// when absent) and run it through
    /// <see cref="string.Format(IFormatProvider, string, object?[])"/> with
    /// <see cref="System.Globalization.CultureInfo.CurrentCulture"/>. The
    /// resolved string is the composite format — each language controls its
    /// own word order via the <c>{0}</c>/<c>{1}</c>… placeholders.
    /// </summary>
    public static string Format(string key, string fallback, params object?[] args)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key, fallback), args);
}
