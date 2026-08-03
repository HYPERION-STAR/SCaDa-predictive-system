using System.IO;
using System.Text.Json;

namespace SCaDaDashboard.Services;

/// <summary>Harita veri kaynağı seçimi (ayarlar penceresinden).</summary>
public enum SourceMode
{
    Otomatik,    // SCaDa_LIVE_URL > SCaDa_HUB_URL > snapshot dosyası > simülasyon
    Simulasyon,
    Snapshot,
    ApiHub,
    Canli,       // canlı telemetri endpoint'i (snapshot topolojisi + HTTP yoklama)
    YerelVeri,   // çıkarılan yerel topoloji (SCaDa_nodes.json + SCaDa_segments.json); yalnız topoloji
}

/// <summary>
/// Uygulama ayarları. Exe klasöründeki settings.json'a yazılır/okunur;
/// dosya yoksa varsayılanlar geçerli (Otomatik kaynak seçimi).
/// </summary>
public sealed class AppSettings
{
    public SourceMode SourceMode { get; set; } = SourceMode.Otomatik;
    public string HubUrl { get; set; } = "";

    /// <summary>Açık tema (varsayılan koyu).</summary>
    public bool LightTheme { get; set; } = false;

    /// <summary>Canlı telemetri endpoint'i (Canli modu; topoloji yine snapshot dosyasından).</summary>
    public string LiveUrl { get; set; } = "http://100.114.223.5:8000/api/SCaDa/live_data";

    /// <summary>Canlı yoklama aralığı (saniye) — SCaDaClient PollIntervalSeconds'a geçer.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Canlı HTTP istek zaman aşımı (saniye) — SCaDaClient TimeoutSeconds'a geçer.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>İstasyon dairesi görünümü: pasta (varsayılan) / ortalama / en kötü.</summary>
    public Pipeline.HealthDisplayMode HealthDisplayMode { get; set; } = Pipeline.HealthDisplayMode.Pie;

    /// <summary>Pasta kenar rengi özeti: en kötü (varsayılan) / ortalama. Yalnız pasta modunda.</summary>
    public Pipeline.HealthAggregate HealthOutlineAggregate { get; set; } = Pipeline.HealthAggregate.Worst;

    /// <summary>Kritik alarm eşiği (sağlık %). Alarm HER ZAMAN en kötü üniteye bakar;
    /// eşik düşürülünce alarm seyrekleşir. Varsayılan 20.</summary>
    public double CriticalHealthThreshold { get; set; } = 20;

    /// <summary>Pasta dilim ayırıcı rengi (#RRGGBB). Boş = tema varsayılanı (Bg ile karıştırma).</summary>
    public string PieSeparatorColorHex { get; set; } = "";

    private static string PathFor() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var p = PathFor();
            if (File.Exists(p))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings();
        }
        catch { /* bozuk dosya → varsayılanlar */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(PathFor(),
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* yazılamazsa ayar yalnızca oturumluk kalır */ }
    }
}
