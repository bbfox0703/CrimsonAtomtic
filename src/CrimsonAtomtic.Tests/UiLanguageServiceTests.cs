using System.Globalization;
using CrimsonAtomtic.Ui.Services;
using Xunit;

namespace CrimsonAtomtic.Tests;

/// <summary>
/// Pure unit tests for <see cref="UiLanguageService"/>'s detection and
/// resolution logic — the parts that don't touch
/// <see cref="Avalonia.Application"/>. The actual
/// <see cref="UiLanguageService.Apply"/> swap is an
/// Avalonia.Application.Resources.MergedDictionaries side effect; it's
/// exercised via the manual smoke test path (Tools → UI Language menu)
/// rather than a unit test, because driving Avalonia in xUnit headlessly
/// adds complexity that isn't worth the coverage for two LOC of list
/// manipulation.
/// </summary>
public sealed class UiLanguageServiceTests
{
    // ------------------------------------------------------------------
    // ResolveActive — settings.UiLanguage wins when it names a supported code
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("en", "en-US", "en")]
    [InlineData("ja", "en-US", "ja")]      // explicit pick beats OS culture
    [InlineData("zh-TW", "ja-JP", "zh-TW")] // explicit pick beats OS culture
    [InlineData("EN", "ja-JP", "en")]      // case-insensitive
    [InlineData("ZH-tw", "ja-JP", "zh-TW")]
    public void ResolveActive_ExplicitPick_WinsOverOsCulture(string settings, string culture, string expected)
    {
        var ci = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, UiLanguageService.ResolveActive(settings, ci));
    }

    // ------------------------------------------------------------------
    // ResolveActive — unsupported / blank settings fall through to OS detect
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null, "en-US", "en")]
    [InlineData(null, "ja-JP", "ja")]
    [InlineData(null, "zh-TW", "zh-TW")]
    [InlineData("", "ja-JP", "ja")]               // empty string == not-set
    [InlineData("fr-FR", "ja-JP", "ja")]          // unknown code => auto-detect
    [InlineData("nonsense", "zh-Hant", "zh-TW")]  // ditto, with zh-Hant culture
    public void ResolveActive_NullOrUnsupported_FallsBackToCulture(string? settings, string culture, string expected)
    {
        var ci = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, UiLanguageService.ResolveActive(settings, ci));
    }

    // ------------------------------------------------------------------
    // DetectFromCulture — Japanese family
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ja", "ja")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ja-Jp-JP", "ja")] // unusual but conceivable culture name
    public void DetectFromCulture_JapaneseFamily_MapsToJa(string culture, string expected)
    {
        var ci = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, UiLanguageService.DetectFromCulture(ci));
    }

    // ------------------------------------------------------------------
    // DetectFromCulture — Traditional Chinese family
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-Hant-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-MO", "zh-TW")]
    public void DetectFromCulture_TraditionalChinese_MapsToZhTw(string culture, string expected)
    {
        var ci = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, UiLanguageService.DetectFromCulture(ci));
    }

    // ------------------------------------------------------------------
    // DetectFromCulture — everything else falls back to English (Simplified
    // Chinese included, since we don't ship a zh-Hans dictionary today).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]      // Simplified Chinese — not shipped → English
    [InlineData("zh-Hans-CN")] // ditto
    [InlineData("zh-SG")]      // Singapore Mandarin — Simplified → English
    [InlineData("ko-KR")]
    public void DetectFromCulture_UnsupportedOrEnglish_FallsBackToEn(string culture)
    {
        var ci = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(UiLanguageService.CodeEn, UiLanguageService.DetectFromCulture(ci));
    }

    // ------------------------------------------------------------------
    // Invariant culture (empty name) — never crashes, always falls through
    // to the English default.
    // ------------------------------------------------------------------

    [Fact]
    public void DetectFromCulture_InvariantCulture_FallsBackToEn()
    {
        Assert.Equal(UiLanguageService.CodeEn, UiLanguageService.DetectFromCulture(CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------
    // IsSupported — case-insensitive membership check.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("en", true)]
    [InlineData("ja", true)]
    [InlineData("zh-TW", true)]
    [InlineData("EN", true)]
    [InlineData("Ja", true)]
    [InlineData("ZH-tw", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("zh-CN", false)]
    [InlineData("fr-FR", false)]
    public void IsSupported_OnlyShippedCodes_AreAccepted(string? code, bool expected)
    {
        Assert.Equal(expected, UiLanguageService.IsSupported(code));
    }
}
