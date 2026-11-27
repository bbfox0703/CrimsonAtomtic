namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// The game's regions as its internal names abbreviate them
/// (<c>Node_Her_…</c>, <c>AbyssRuins_Dem_0019</c>). The game data holds no
/// region for a faction node or a field gimmick, so callers read the code
/// out of an internal name; the result is a guess and the UI says so.
/// Regions are keyed by their English name.
/// </summary>
internal static class GameRegions
{
    public const string Abyss = "Abyss";

    /// <summary>English region name for an internal-name code, or null when the code names none.</summary>
    public static string? FromCode(string code) => code switch
    {
        "Her" or "Hernand" => "Hernand",
        "Dem" => "Demeniss",
        "Del" => "Delesyia",
        "Crim" or "Crimson" or "CD" => "Crimson Desert",
        "Kwe" => "Kweiden",
        "Cal" => "Calphade",
        "Pailune" or "Pai" => "Pailune",
        "Ocean" or "Pywel" => "Great Ocean",
        "Abyss" or "Abyssone" => Abyss,
        _ => null,
    };

    /// <summary>
    /// "Hernand / 埃爾南德": the English name plus the game's faction-group
    /// name in the secondary language where one exists (Kweiden and
    /// Calphade have none and stay English).
    /// </summary>
    public static string Label(string region, IReadOnlyDictionary<string, string?> groupNames) =>
        groupNames.TryGetValue(region, out var secondary) && secondary is not null && secondary != region
            ? $"{region} / {secondary}"
            : region;
}
