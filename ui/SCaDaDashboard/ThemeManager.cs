using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SCaDaDashboard;


/// <summary>
/// Koyu/açık tema arasında geçiş. App.xaml'deki paylaşılan fırça kaynaklarının
/// <c>.Color</c>'ını değiştirir (StaticResource referansları anında güncellenir)
/// ve harita kontrolünün paletini uygular. Sağlık/akış gibi anlamsal canlı
/// renkler her iki temada da aynı kalır.
/// </summary>
public static class ThemeManager
{
    public static AppThemeMode Current { get; private set; } = AppThemeMode.Dark;

    /// <summary>Tema değişince tetiklenir (harita yeniden çizilsin diye).</summary>
    public static event Action? Changed;

    // (kaynak anahtarı, koyu, açık)
    private static readonly (string Key, string Dark, string Light)[] BrushMap =
    {
        ("BgBrush",     "#0F1620", "#EEF2F6"),
        ("PanelBrush",  "#1A2634", "#FFFFFF"),
        ("CardBrush",   "#1E2C3D", "#F6F9FC"),
        ("StrokeBrush", "#2E4258", "#D3DCE6"),
        ("TextBrush",   "#E6EEF6", "#16202B"),
        ("MutedBrush",  "#9AAFC4", "#5B6B7C"),
        ("TrackBrush",  "#12202E", "#E2E9F0"),
        ("AccentBrush", "#4ECDC4", "#12988F"),
    };

    public static void Apply(AppThemeMode mode)
    {
        Current = mode;
        bool light = mode == AppThemeMode.Light;

        var res = Application.Current?.Resources;
        if (res != null)
        {
            // Kaynak fircalari XAML'de dondurulmus (read-only) — mutasyon yerine
            // yeni fircayla DEGISTIR; DynamicResource referanslari otomatik guncellenir.
            foreach (var (key, dark, lightHex) in BrushMap)
                res[key] = new SolidColorBrush(Parse(light ? lightHex : dark));

            res["CardShadow"] = new DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 3, Direction = 270,
                Color = Parse(light ? "#334155" : "#000000"),
                Opacity = light ? 0.18 : 0.35,
            };
        }

        Pipeline.PipelineMapControl.ApplyTheme(light);
        Changed?.Invoke();
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
