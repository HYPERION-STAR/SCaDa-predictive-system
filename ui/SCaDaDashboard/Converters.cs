using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SCaDaDashboard;

/// <summary>Uyari onem seviyesini renge cevirir (uyari paneli).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Critical = Frozen(0xE7, 0x4C, 0x3C);
    private static readonly SolidColorBrush Warning = Frozen(0xF1, 0xC4, 0x0F);
    private static readonly SolidColorBrush Info = Frozen(0x3A, 0x9B, 0xDC);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Kritik" => Critical,
            "Uyarı" => Warning,
            _ => Info,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Makine ID'sini (MTR-01, PMP-02, ...) sabit ve ayirt edici bir renge cevirir.
/// Boylece uyari panelinde her makinenin mesaji renginden taninir.
/// Ayni palet ve hash tasarim sandbox'inda da kullanilir (renkler eslessin).
/// </summary>
public sealed class MachineIdToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush[] Palette =
    {
        Frozen(0x4E, 0x9B, 0xF5), // mavi
        Frozen(0xF5, 0x8A, 0x4E), // turuncu
        Frozen(0x57, 0xC7, 0xA5), // teal
        Frozen(0xC5, 0x8A, 0xF5), // mor
        Frozen(0xF5, 0x5E, 0x8A), // pembe
        Frozen(0x9B, 0xD6, 0x4E), // yesil
        Frozen(0xF5, 0xC8, 0x4E), // amber
        Frozen(0x4E, 0xC0, 0xF5), // camgobegi
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        int sum = 0;
        foreach (char c in s) sum += c;
        return Palette[((sum % Palette.Length) + Palette.Length) % Palette.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Durum rengini soluk zemin tonuna cevirir (~%18 opaklik).
/// Durum rozeti: zemin = soluk ton, yazi = rengin kendisi.
/// </summary>
public sealed class TintBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush b)
        {
            var t = new SolidColorBrush(Color.FromArgb(0x2E, b.Color.R, b.Color.G, b.Color.B));
            t.Freeze();
            return t;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Sayiyi "%73" gibi bicimler (kultur bagimsiz).</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double d = value is double v ? v : 0;
        return $"%{d:0}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
