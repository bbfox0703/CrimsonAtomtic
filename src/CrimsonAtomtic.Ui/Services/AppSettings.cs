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
    /// Optional directory holding item icons named <c>&lt;ItemKey&gt;.webp</c>.
    /// When set, the Item Picker / inventory views show a small icon
    /// next to each item. <c>null</c> falls back to the default probe
    /// chain (<c>&lt;exe-dir&gt;/IconCache/</c> → empty). Pearl Abyss
    /// owns the icon artwork so we deliberately don't bundle them —
    /// users point at their own extracted folder (e.g. the
    /// <c>icons_local/</c> directory the reference repo
    /// <c>CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS</c> ships).
    /// </summary>
    [JsonPropertyName("icon_cache_directory")]
    public string? IconCacheDirectory { get; init; }
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
