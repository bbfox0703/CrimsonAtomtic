using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrimsonAtomtic.Ui.Platform;

/// <summary>
/// Locate a Crimson Desert install on Epic Games Store by walking the
/// manifest <c>.item</c> files the Epic launcher writes for every
/// installed title.
/// </summary>
/// <remarks>
/// <para>
/// Manifests live at <c>%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\</c>
/// — one JSON file per installed game. Relevant fields:
/// <code>
/// {
///   "DisplayName": "Crimson Desert",
///   "AppName": "...",
///   "InstallLocation": "C:\\Program Files\\Epic Games\\CrimsonDesert",
///   ...
/// }
/// </code>
/// We match on <c>DisplayName</c> containing "Crimson Desert" (case-
/// insensitive) and fall back to <c>AppName</c> containing
/// "CrimsonDesert". The matched <c>InstallLocation</c> is then validated
/// against the same <c>0020\0.pamt</c> witness file the Steam probe uses,
/// so a stale manifest entry (game uninstalled but Epic forgot to remove
/// the file) doesn't yield a phantom install.
/// </para>
/// <para>
/// AOT-safe: uses <see cref="JsonSerializerContext"/> source generation
/// for the manifest schema. The launcher writes plenty of other fields
/// we don't care about — they're ignored at deserialize time.
/// </para>
/// </remarks>
public static class EpicManifestProbe
{
    /// <summary>Subpath under <c>%PROGRAMDATA%</c> where Epic stores its install manifests.</summary>
    private const string ManifestsSubPath = @"Epic\EpicGamesLauncher\Data\Manifests";

    /// <summary>Substring (case-insensitive) that identifies the game in <c>DisplayName</c>.</summary>
    private const string DisplayNameNeedle = "Crimson Desert";

    /// <summary>Substring (case-insensitive) that identifies the game in <c>AppName</c>.</summary>
    private const string AppNameNeedle = "CrimsonDesert";

    /// <summary>
    /// Find a Crimson Desert install via Epic's launcher manifests.
    /// Returns the validated <c>InstallLocation</c>, or <c>null</c> when
    /// Epic isn't installed / no matching manifest exists / the
    /// referenced folder no longer contains the witness file.
    /// </summary>
    public static string? FindCrimsonDesertInstall()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(programData))
        {
            return null;
        }
        var manifestDir = Path.Combine(programData, ManifestsSubPath);
        if (!Directory.Exists(manifestDir))
        {
            return null;
        }
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(manifestDir, "*.item");
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        foreach (var manifestPath in manifests)
        {
            var entry = TryReadManifest(manifestPath);
            if (entry is null)
            {
                continue;
            }
            if (!IsCrimsonDesertManifest(entry))
            {
                continue;
            }
            var install = entry.InstallLocation;
            if (!string.IsNullOrWhiteSpace(install)
                && SteamLibraryProbe.LooksLikeCrimsonDesertInstall(install))
            {
                return install;
            }
        }
        return null;
    }

    /// <summary>
    /// Decide whether <paramref name="entry"/> identifies a Crimson
    /// Desert install (by <c>DisplayName</c> containing "Crimson Desert"
    /// or <c>AppName</c> containing "CrimsonDesert"). Exposed so unit
    /// tests can pin the matching rule without needing real manifest
    /// files on disk.
    /// </summary>
    public static bool IsCrimsonDesertManifest(EpicManifestEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.DisplayName)
            && entry.DisplayName.Contains(DisplayNameNeedle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(entry.AppName)
            && entry.AppName.Contains(AppNameNeedle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static EpicManifestEntry? TryReadManifest(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(
                stream,
                EpicManifestJsonContext.Default.EpicManifestEntry);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Minimal Epic manifest schema — only the fields we use. The launcher
/// writes ~30 other properties; ignoring them keeps the deserializer
/// happy and the AOT trim graph small.
/// </summary>
public sealed record EpicManifestEntry
{
    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("AppName")]
    public string? AppName { get; init; }

    [JsonPropertyName("InstallLocation")]
    public string? InstallLocation { get; init; }
}

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for AOT-safe
/// Epic manifest deserialization.
/// </summary>
[JsonSerializable(typeof(EpicManifestEntry))]
public sealed partial class EpicManifestJsonContext : JsonSerializerContext;
