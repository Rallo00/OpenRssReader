using System.Windows.Media;
using System.Collections.Concurrent;

namespace OpenRssReader.ViewModels;

public static class BrushFactory
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushesByColor = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SolidColorBrush> HeroBrushesByColor = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush CreateBrush(string hex)
    {
        return BrushesByColor.GetOrAdd(hex, static value =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        });
    }

    public static SolidColorBrush CreateHeroBrush(string hex)
    {
        return HeroBrushesByColor.GetOrAdd(hex, static value =>
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            var mixed = Color.FromRgb(
                (byte)Math.Min(255, color.R + 20),
                (byte)Math.Min(255, color.G + 18),
                (byte)Math.Min(255, color.B + 24));
            var brush = new SolidColorBrush(mixed);
            brush.Freeze();
            return brush;
        });
    }
}
