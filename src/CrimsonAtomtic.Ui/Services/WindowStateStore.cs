using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Last-saved main-window placement: top-left in PHYSICAL pixels (Avalonia
/// <c>PixelPoint</c> units, matching <c>Window.Position</c>) and size in DIPs
/// (Avalonia <c>Window.Width</c>/<c>Height</c> units). Mixed units on purpose —
/// each field is stored in the same unit Avalonia consumes it in, so a restore
/// on the same monitor layout round-trips exactly.
/// </summary>
public readonly record struct WindowStateRecord(int X, int Y, double Width, double Height, bool Maximized);

/// <summary>
/// Persists the main window's position / size / maximized state to a small
/// text file at <c>%LOCALAPPDATA%\CrimsonAtomtic\window-state.txt</c> (peer to
/// <c>settings.json</c>). Plain <c>key=value</c> lines — no JSON reflection, so it
/// is trivially Native-AOT safe (mirrors <see cref="AppSettingsStore"/>). Corrupt /
/// partial files load as null so the app falls back to its default placement.
///
/// <para>Ported from UE5CEDumper's window-restore design, adapted to
/// <c>IPlatformPaths.LocalAppDataDirectory</c> (which is already app-scoped, so
/// no app-name folder is re-appended).</para>
/// </summary>
public sealed class WindowStateStore
{
    public const string FileName = "window-state.txt";
    private readonly string _path;

    /// <param name="localAppDataDirectory">The app-scoped per-user directory
    /// (<c>IPlatformPaths.LocalAppDataDirectory</c>, e.g.
    /// <c>%LOCALAPPDATA%\CrimsonAtomtic</c>).</param>
    public WindowStateStore(string localAppDataDirectory)
    {
        _path = Path.Combine(localAppDataDirectory, FileName);
    }

    /// <summary>Load the saved placement, or null if absent / unreadable / corrupt.</summary>
    public WindowStateRecord? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return Parse(File.ReadAllLines(_path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persist the placement (best-effort; failures are swallowed).</summary>
    public void Save(WindowStateRecord record)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(_path, Format(record));
        }
        catch
        {
            // best-effort persistence
        }
    }

    // ── Pure (re)serialization — unit-tested, no IO ──────────────────────────

    /// <summary>Render a record to the on-disk line set (invariant culture).</summary>
    public static string[] Format(WindowStateRecord r) => new[]
    {
        "# CrimsonAtomtic main window state",
        $"x={r.X.ToString(CultureInfo.InvariantCulture)}",
        $"y={r.Y.ToString(CultureInfo.InvariantCulture)}",
        $"w={r.Width.ToString(CultureInfo.InvariantCulture)}",
        $"h={r.Height.ToString(CultureInfo.InvariantCulture)}",
        $"max={(r.Maximized ? 1 : 0)}",
    };

    /// <summary>
    /// Parse the line set back into a record. Returns null when any required
    /// field (x/y/w/h) is missing or the size is non-positive — callers treat
    /// null as "no usable saved state, use defaults".
    /// </summary>
    public static WindowStateRecord? Parse(IReadOnlyList<string> lines)
    {
        int? x = null, y = null;
        double? w = null, h = null;
        bool max = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq).Trim();
            var val = line.Substring(eq + 1).Trim();

            switch (key)
            {
                case "x": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xi)) x = xi; break;
                case "y": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yi)) y = yi; break;
                case "w": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var wd)) w = wd; break;
                case "h": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var hd)) h = hd; break;
                case "max": max = val == "1" || val.Equals("true", System.StringComparison.OrdinalIgnoreCase); break;
            }
        }

        if (x is null || y is null || w is null || h is null) return null;
        if (w.Value < 1 || h.Value < 1) return null;
        return new WindowStateRecord(x.Value, y.Value, w.Value, h.Value, max);
    }
}
