using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace CrimsonAtomtic.Ui.Services;

/// <summary>
/// Maps a <c>uint</c> ItemKey to its <see cref="Bitmap"/> from the
/// active <see cref="IconProvider"/>, or <c>null</c> when no icon is
/// available. Wired in XAML as a singleton converter on
/// <see cref="DataGridTemplateColumn"/> Image bindings so each
/// virtualized row resolves its icon lazily — Avalonia's DataGrid
/// only materialises visible rows, so we only ever pay for what the
/// user actually sees.
///
/// <para>
/// The converter holds a reference to the live
/// <see cref="LocalizationProvider"/>'s
/// <see cref="LocalizationProvider.Icons"/> via a static
/// <see cref="Instance"/>. App startup sets it once; every
/// converter use thereafter is a cheap dictionary lookup. Avalonia
/// compiled bindings can't construct converters with constructor
/// args, so the static-singleton + setter pattern is the path of
/// least friction.
/// </para>
/// </summary>
public sealed class ItemKeyToIconConverter : IValueConverter
{
    public static readonly ItemKeyToIconConverter Instance = new();

    /// <summary>
    /// Active icon provider. Set by App startup after
    /// <see cref="LocalizationProvider.ConfigureIconProvider"/>
    /// runs. Null before bootstrap (and in design-time previews)
    /// — the converter quietly returns <c>null</c> in that case.
    /// </summary>
    public static IconProvider? Provider { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Provider is not { IsAvailable: true } provider)
        {
            return null;
        }
        var key = value switch
        {
            uint u  => u,
            int i when i >= 0 => (uint)i,
            ulong ul when ul <= uint.MaxValue => (uint)ul,
            long  l when l >= 0 && l <= uint.MaxValue => (uint)l,
            string s when uint.TryParse(s, NumberStyles.Integer,
                                         CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0u,
        };
        if (key == 0)
        {
            return null;
        }
        return provider.GetItemIcon(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Icon binding is one-way.");
}
