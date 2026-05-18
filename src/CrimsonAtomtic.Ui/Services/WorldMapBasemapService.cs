using Avalonia.Media.Imaging;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Loads a user-picked basemap image for the Tools → World Map dialog.
/// Replaces the previous "extract from PAZ + stitch parchment composite"
/// pipeline — the dialog now treats the basemap as an arbitrary user-
/// supplied image layer, and the marker projection is done purely
/// against the canonical <see cref="WorldMapAffine.Canonical"/> constants
/// scaled to whatever display canvas the dialog is using.
///
/// <para>
/// The service itself is stateless — it just validates the path and
/// constructs the <see cref="Bitmap"/>. The dialog's view-model holds
/// the loaded bitmap for the lifetime of the window; settings.json
/// holds only the file path so the dialog can rehydrate the same map
/// on next launch.
/// </para>
/// </summary>
public static class WorldMapBasemapService
{
    /// <summary>
    /// Try loading <paramref name="path"/> as a <see cref="Bitmap"/>.
    /// Returns <c>null</c> on any failure (missing file, unsupported
    /// format, IO error) — best-effort so a stale settings.json entry
    /// can't crash the dialog open.
    /// </summary>
    public static Bitmap? TryLoad(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}
