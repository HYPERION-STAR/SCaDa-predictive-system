using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace SCaDaDashboard;

/// <summary>
/// Pencere cilası: Win11 koyu başlık çubuğu + yuvarlak köşeler (DWM) ve
/// açılışta yumuşak belirme. Eski Windows sürümlerinde DWM çağrıları
/// sessizce yok sayılır (öznitelik desteklenmiyorsa hata dönmez/önemsenmez).
/// </summary>
public static class WindowFx
{
    private const int DwmaUseImmersiveDarkMode = 20; // koyu başlık çubuğu
    private const int DwmaWindowCornerPreference = 33; // 2 = yuvarlak

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window w)
    {
        w.SourceInitialized += (_, _) => SetTitleBar(w);

        // Tema degisince baslik cubugunu (koyu/acik) guncelle.
        void OnTheme() => SetTitleBar(w);
        ThemeManager.Changed += OnTheme;
        w.Closed += (_, _) => ThemeManager.Changed -= OnTheme;

        // Acilista 200ms yumusak belirme.
        w.Opacity = 0;
        w.Loaded += (_, _) => w.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    // Baslik cubugu: koyu temada koyu, acik temada acik + yuvarlak koseler.
    private static void SetTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int dark = ThemeManager.Current == AppThemeMode.Dark ? 1 : 0;
            DwmSetWindowAttribute(h, DwmaUseImmersiveDarkMode, ref dark, sizeof(int));
            int round = 2;
            DwmSetWindowAttribute(h, DwmaWindowCornerPreference, ref round, sizeof(int));
        }
        catch { /* eski Windows: cila yok, islev ayni */ }
    }
}
