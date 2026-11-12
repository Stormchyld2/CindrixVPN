using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CindrixVPN.Common;

/// <summary>true -> a check mark glyph, false -> a cross glyph (Segoe MDL2 Assets).</summary>
public sealed class TickGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -> green, false -> red.</summary>
public sealed class TickBrushConverter : IValueConverter
{
    private static readonly Brush Green = Make(0x34, 0xE2, 0x7A);
    private static readonly Brush Red = Make(0xFF, 0x4D, 0x5E);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Green : Red;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
