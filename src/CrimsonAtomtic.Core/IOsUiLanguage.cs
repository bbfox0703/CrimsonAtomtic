namespace CrimsonAtomtic.Core;

/// <summary>
/// The language the operating system's UI is set to, for picking the
/// editor's UI language when the user has not chosen one. Behind an
/// interface because each OS reports it differently (Rule 4).
/// </summary>
public interface IOsUiLanguage
{
    /// <summary>
    /// A language tag such as <c>"ja"</c>, <c>"zh-TW"</c> or <c>"en-US"</c>,
    /// or null when the OS does not say.
    /// </summary>
    string? PreferredLanguageTag();
}
