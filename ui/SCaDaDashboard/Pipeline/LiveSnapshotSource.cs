using System.Net.Http;
using Microsoft.Extensions.Options;
using SCaDaClient.Options;
using SCaDaClient.Parsing;
using SCaDaClient.Services;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Canlı telemetri kaynağı — veri yolu Kişi 2'nin resmi istemcisi (SCaDaClient):
/// LiveDataService arka planda /api/SCaDa/live_data'yı yoklar (PollIntervalSeconds),
/// SnapshotReceived olayı tipli DTO'ları getirir; TelemetryAdapter bunları UI'ın
/// sensör sözlüğüne çevirir ve telemetri atomik değiştirilir. Topoloji
/// (düğüm/segment/koordinat/ünite) kurucuya dışarıdan verilir (API veya dosya).
///
/// Sağlık/RUL/akış/sızıntı türetme mantığı SnapshotSource ile aynıdır (proxy).
/// Endpoint erişilemezse istemci turu atlar, SON İYİ telemetri korunur —
/// harita her koşulda çalışır. Kaynak değişiminde Dispose() yoklamayı durdurur.
/// </summary>
public sealed class LiveSnapshotSource : SnapshotSource, IDisposable
{
    private readonly SCaDaApiClient _api;      // HttpClient'ın sahibi
    private readonly LiveDataService _service; // arka plan yoklayıcı (istemcinin tasarımı)
    private readonly NodeDetailClient _detail; // talep üzerine B ucu (/node/{id})
    private readonly ModelHealthClient _model; // Kişi 1 tahmin servisi (/hub_state) — model sağlık/RUL

    public LiveSnapshotSource(
        SnapshotData topology, string baseUrl,
        int pollSeconds = 5, int timeoutSeconds = 15) : base(topology)
    {
        // İstemcinin tüm seçenekleri (BaseUrl + poll aralığı + zaman aşımı) uygulama
        // ayarlarından beslenir — hiçbiri artık sabit kodlu değil.
        var opt = new SCaDaApiOptions
        {
            BaseUrl = baseUrl,
            PollIntervalSeconds = pollSeconds,
            TimeoutSeconds = timeoutSeconds,
        };
        var http = new HttpClient
        {
            BaseAddress = new Uri(opt.BaseUrl),
            Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds),
        };
        _api = new SCaDaApiClient(http, new SCaDaJsonParser());
        _detail = new NodeDetailClient(_api);
        // İstemcinin kendi günlüğünü diske al (poll'un istemci üzerinden çalıştığı
        // çalışma anında görünür olsun; hata turları da yakalanır).
        var log = new FileLogger<LiveDataService>(
            System.IO.Path.Combine(AppContext.BaseDirectory, "SCaDaclient.log"));
        _service = new LiveDataService(_api, Options.Create(opt), null, log);
        _service.SnapshotReceived += live =>
        {
            var maps = TelemetryAdapter.ToSensorMaps(live);
            SetTelemetry(maps.Values, maps.Quality); // değerler + kalite birlikte
        };
        _ = _service.StartAsync(CancellationToken.None); // ilk çekiş hemen, sonra periyodik

        // Model sağlık/RUL kaynağı: tahmin servisini (/hub_state) kendi timer'ıyla yoklar.
        // Erişilemezse sözlük boş kalır → TryModelHealth false → proxy'ye düşülür.
        _model = new ModelHealthClient(pollSeconds: pollSeconds);
    }

    // Tick() kasıtlı override edilmez: yoklamayı UI değil LiveDataService zamanlar.

    /// <summary>Canlı kaynakta ünite sağlığı/RUL'u modelden gelir (varsa).</summary>
    protected override bool TryModelHealth(string unitId, out double health, out double rul)
    {
        if (_model.TryGet(unitId, out var v)) { health = v.Health; rul = v.Rul; return true; }
        health = 0; rul = 0; return false;
    }

    /// <summary>
    /// Talep üzerine B ucunu (/api/SCaDa/node/{id}) çeker — haritada bir düğüme
    /// tıklanınca. Tek çağrıda NodeDetail'in TAMAMI tüketilir:
    ///   • health_state → sunucunun yetkili sağlık değerlendirmesi (döner)
    ///   • telemetry     → varsa taze düğüm telemetrisi canlı haritaya bindirilir
    ///                     (sunucu genelde boş [] gönderir; doluysa poll'dan tazedir)
    /// Canlı poll (/live_data) health_state taşımaz; yalnız bu uçta gelir. Hata/boşsa
    /// null döner ve UI titreşim proxy'sine düşer. Bloklamaz — çağıran await eder.
    /// </summary>
    public async Task<string?> FetchNodeDetailAsync(string nodeId)
    {
        try
        {
            var d = await _detail.LoadAsync(nodeId);
            if (d.Telemetry.Count > 0)
            {
                var maps = TelemetryAdapter.ToSensorMaps(d.Telemetry);
                MergeTelemetry(maps.Values, maps.Quality);
            }
            return string.IsNullOrWhiteSpace(d.HealthState) ? null : d.HealthState;
        }
        catch { return null; } // uç erişilemez/id yok — proxy'ye düş
    }

    public void Dispose()
    {
        _service.Dispose(); // yoklama döngüsünü iptal eder (BackgroundService)
        _api.Dispose();
        _model.Dispose();   // model yoklama timer'ını + HttpClient'ı kapatır
    }
}
