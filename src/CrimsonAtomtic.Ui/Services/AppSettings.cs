using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// User-tweakable settings persisted to <c>%LOCALAPPDATA%\CrimsonAtomtic\settings.json</c>.
/// Kept deliberately tiny and AOT-safe via a source-generated
/// <see cref="JsonSerializerContext"/> (no runtime reflection).
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Optional second-language PALOC code (e.g. <c>"zho-tw"</c>). When
    /// set, the fields DataGrid resolves item IDs into "English / Local"
    /// pairs. <c>null</c> means English-only.
    /// </summary>
    [JsonPropertyName("secondary_language")]
    public string? SecondaryLanguage { get; init; }

    /// <summary>
    /// True when the left-side Save Summary panel is collapsed to a
    /// narrow rail (so the main DataGrid claims the freed horizontal
    /// space). Toggled via the chevron button in the summary header.
    /// <c>null</c> / missing means "expanded" — the default state.
    /// </summary>
    [JsonPropertyName("summary_collapsed")]
    public bool? SummaryCollapsed { get; init; }

    /// <summary>
    /// User-picked base font size for the main window (point units).
    /// Avalonia cascades the Window's <c>FontSize</c> through the
    /// visual tree so this affects every label / DataGrid cell /
    /// menu in one shot. Clamped to <see cref="MinFontSize"/>..
    /// <see cref="MaxFontSize"/> on read so a hand-edited
    /// settings.json can't push values that make the UI unusable.
    /// <c>null</c> / missing means <see cref="DefaultFontSize"/>.
    /// </summary>
    [JsonPropertyName("font_size")]
    public double? FontSize { get; init; }

    /// <summary>
    /// Save platform the user last opened a save from
    /// (<c>"Steam"</c> / <c>"Epic"</c> / <c>"GamePass"</c>). On next
    /// launch, the Open Save dialog defaults to this platform's save
    /// root if it still exists; otherwise it falls back to the platform
    /// with the most recently modified save. <c>null</c> / missing means
    /// "haven't successfully opened a save yet — pick most-recent".
    /// String rather than enum so a future <see cref="CrimsonAtomtic.Core.SavePlatform"/>
    /// value can be added without breaking the deserializer on old settings files.
    /// </summary>
    [JsonPropertyName("preferred_platform")]
    public string? PreferredPlatform { get; init; }

    /// <summary>
    /// User-picked Crimson Desert install folder. Takes precedence over
    /// every auto-probe in <see cref="CrimsonAtomtic.Core.IPlatformPaths.GameInstallRoot"/>.
    /// Set via Tools → Set Game Install Folder…; the picker validates
    /// that the chosen directory contains the <c>0020\0.pamt</c>
    /// witness file before persisting. <c>null</c> / missing means
    /// "auto-probe Steam libraryfolders.vdf + Epic manifest". Useful
    /// for Game Pass users (WindowsApps access restrictions), unusual
    /// Steam library layouts, or asset folders manually copied out.
    /// </summary>
    [JsonPropertyName("game_install_root")]
    public string? GameInstallRoot { get; init; }

    /// <summary>Default font size when the settings field is unset.</summary>
    public const double DefaultFontSize = 14.0;

    /// <summary>Smallest font size we accept (anything smaller is hard to read on hi-DPI).</summary>
    public const double MinFontSize = 10.0;

    /// <summary>Largest font size we accept (beyond this, DataGrids start truncating columns).</summary>
    public const double MaxFontSize = 22.0;
}

/// <summary>
/// Source-generated JSON context for <see cref="AppSettings"/>.
/// Required for AOT — System.Text.Json otherwise reflects at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext;

/// <summary>
/// Read / write helpers for <see cref="AppSettings"/>. Best-effort: any
/// failure (missing file, malformed JSON, IO error) collapses to the
/// default empty settings.
/// </summary>
public static class AppSettingsStore
{
    public const string FileName = "settings.json";

    /// <summary>Load settings from <paramref name="localAppDataDirectory"/>. Never throws.</summary>
    public static AppSettings Load(string localAppDataDirectory)
    {
        try
        {
            var path = Path.Combine(localAppDataDirectory, FileName);
            if (!File.Exists(path))
            {
                return new AppSettings();
            }
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Persist <paramref name="settings"/>. Returns <c>true</c> on success.</summary>
    public static bool TrySave(string localAppDataDirectory, AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(localAppDataDirectory);
            var path = Path.Combine(localAppDataDirectory, FileName);
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
