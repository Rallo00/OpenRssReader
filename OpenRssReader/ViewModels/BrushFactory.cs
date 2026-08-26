using System.Windows.Media;

namespace OpenRssReader.ViewModels;

public static class BrushFactory
{
    public static SolidColorBrush CreateBrush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public static SolidColorBrush CreateHeroBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var mixed = Color.FromRgb(
            (byte)Math.Min(255, color.R + 20),
            (byte)Math.Min(255, color.G + 18),
            (byte)Math.Min(255, color.B + 24));
        return new SolidColorBrush(mixed);
    }
}
