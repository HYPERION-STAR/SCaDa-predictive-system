using System.Windows;
using SCaDaDashboard.Services;

namespace SCaDaDashboard;

/// <summary>
/// Ayarlar penceresi: veri kaynağı seçimi. "Uygula" ile ayar kalıcı yazılır
/// (settings.json) ve DialogResult=true döner; MapWindow kaynağı anında değiştirir.
/// </summary>
public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    private readonly bool _origLight; // Vazgeç'te temayi geri almak icin

    public SettingsWindow(AppSettings current, string? snapshotPath, bool localDataAvailable = false)
    {
        InitializeComponent();
        WindowFx.Apply(this);
        Result = current;
        _origLight = current.LightTheme;
        LightThemeBox.IsChecked = current.LightTheme;

        ModeAuto.IsChecked = current.SourceMode == SourceMode.Otomatik;
        ModeSim.IsChecked = current.SourceMode == SourceMode.Simulasyon;
        ModeSnap.IsChecked = current.SourceMode == SourceMode.Snapshot;
        ModeLocal.IsChecked = current.SourceMode == SourceMode.YerelVeri;
        ModeLive.IsChecked = current.SourceMode == SourceMode.Canli;
        ModeHub.IsChecked = current.SourceMode == SourceMode.ApiHub;
        HubUrlBox.Text = current.HubUrl;
        LiveUrlBox.Text = current.LiveUrl;
        ThresholdSlider.Value = current.CriticalHealthThreshold;

        SnapInfo.Text = snapshotPath != null
            ? $"Bulunan dosya: {snapshotPath}"
            : "Snapshot dosyası bulunamadı (*live_snapshot.json)";
        ModeSnap.IsEnabled = snapshotPath != null;

        // Yerel Veri: yalnız çıkarılan topoloji dosyaları bulunduysa seçilebilir.
        ModeLocal.IsEnabled = localDataAvailable;
        if (!localDataAvailable)
            LocalInfo.Text = "SCaDa_nodes.json + SCaDa_segments.json bulunamadı";

        // Canlı: topoloji API'den (/nodes + /segments), telemetri /live_data'dan.
        // API erişilemezse snapshot dosyasına düşer; her koşulda seçilebilir.
        LiveInfo.Text = "Telemetri + topoloji canlı API'den (/nodes, /segments, /live_data)";
        ModeLive.IsEnabled = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var mode = ModeSim.IsChecked == true ? SourceMode.Simulasyon
                 : ModeSnap.IsChecked == true ? SourceMode.Snapshot
                 : ModeLocal.IsChecked == true ? SourceMode.YerelVeri
                 : ModeLive.IsChecked == true ? SourceMode.Canli
                 : ModeHub.IsChecked == true ? SourceMode.ApiHub
                 : SourceMode.Otomatik;

        string url = HubUrlBox.Text.Trim();
        string liveUrl = LiveUrlBox.Text.Trim();
        if (mode == SourceMode.ApiHub && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ErrorText.Text = "Geçerli bir hub adresi girin (ör. http://localhost:8000).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (mode == SourceMode.Canli && !Uri.TryCreate(liveUrl, UriKind.Absolute, out _))
        {
            ErrorText.Text = "Geçerli bir canlı adres girin (ör. http://host:8000/api/SCaDa/live_data).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new AppSettings
        {
            SourceMode = mode, HubUrl = url, LiveUrl = liveUrl,
            LightTheme = LightThemeBox.IsChecked == true,
            // canlı istemci ayarları korunur (aksi halde Uygula varsayılana döndürürdü)
            PollIntervalSeconds = Result.PollIntervalSeconds,
            TimeoutSeconds = Result.TimeoutSeconds,
            // sağlık görünümü üst bar menüsünden yönetilir; burada korunur
            HealthDisplayMode = Result.HealthDisplayMode,
            HealthOutlineAggregate = Result.HealthOutlineAggregate,
            CriticalHealthThreshold = ThresholdSlider.Value,
            PieSeparatorColorHex = Result.PieSeparatorColorHex,
        };
        Result.Save();
        DialogResult = true;
    }

    // Canli onizleme: onay kutusu degisince temayi hemen uygula.
    private void Theme_Changed(object sender, RoutedEventArgs e)
        => ThemeManager.Apply(LightThemeBox.IsChecked == true ? AppThemeMode.Light : AppThemeMode.Dark);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Onizlenen tema geri alinir.
        ThemeManager.Apply(_origLight ? AppThemeMode.Light : AppThemeMode.Dark);
        DialogResult = false;
    }
}
