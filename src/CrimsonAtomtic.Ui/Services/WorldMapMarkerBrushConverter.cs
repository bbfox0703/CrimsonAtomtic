using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Resolves a marker's fill brush. Single-value mode returns the stock
/// kind colour (active char = gold, mercenary = sky blue, gimmick = gray);
/// multi-value mode `(Kind, FieldInfoKey, UseRegionColoring)` swaps in
/// the per-region HSL hue when the toggle is on. AOT-safe: no
/// reflection, conversion is a switch on the typed input.
/// </summary>
public sealed class WorldMapMarkerBrushConverter : IMultiValueConverter
{
    public static readonly WorldMapMarkerBrushConverter Instance = new();

    private static readonly SolidColorBrush ActiveCharBrush = new(Color.FromRgb(0xFF, 0xC0, 0x33)); // gold
    private static readonly SolidColorBrush MercenaryBrush = new(Color.FromRgb(0x33, 0xA1, 0xFD));  // sky blue
    private static readonly SolidColorBrush GimmickBrush = new(Color.FromRgb(0xC8, 0xC8, 0xC8));    // gray

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values: [Kind, FieldInfoKey, UseRegionColoring]
        if (values.Count < 3)
        {
            return GimmickBrush;
        }
        var kind = values[0] is WorldMapMarkerKind k ? k : WorldMapMarkerKind.Gimmick;
        var useRegion = values[2] is bool b && b;
        if (!useRegion)
        {
            return kind switch
            {
                WorldMapMarkerKind.ActiveChar => ActiveCharBrush,
                WorldMapMarkerKind.Mercenary => MercenaryBrush,
                _ => GimmickBrush,
            };
        }
        var fieldKey = values[1] is uint uk ? uk : 0u;
        var (r, g, blue) = WorldMapViewModel.RegionColor(fieldKey);
        return new SolidColorBrush(Color.FromRgb(r, g, blue));
    }
}
