using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using SCaDaClient.Models;
using SCaDaClient.Options;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Services;

/// <summary>
/// PredictionService ÔÇö LiveDataService.SnapshotReceived'a abone olur,
/// sliding window tutar, tahmin ├ºa─ƒr─▒s─▒ yapar, kural tabanl─▒ sa─ƒl─▒k ├╝retir.
/// ┬º4.1, ┬º4.3, ┬º5, ┬º12 D-S1 ENTEGRASYON_TAHMIN_SISTEMI.md ile uyumlu.
/// E2: Circuit breaker (Polly v8 ResiliencePipeline).
/// E4: SemaphoreSlim ile tur ├Ârt├╝┼ƒme kilidi.
/// </summary>
public sealed class PredictionService
{
    private readonly PredictionApiClient _predApi;
    private readonly ILogger<PredictionService> _log;
    private readonly PredictionApiOptions _predOpt;

    // Sliding window ÔÇö entity_id ba┼ƒ─▒na ge├ºmi┼ƒ penceresi (┬º4.1)
    private readonly ConcurrentDictionary<string, Queue<TelemetryBase>> _history = new();
    private const int WindowSize = 50;

    // UI + DB abone olaca─ƒ─▒ olay
    public event Action<PredictionResult>? PredictionReady;

    // E2: Circuit breaker ÔÇö servis down bilinince pe┼ƒ pe┼ƒe ├ºa─ƒr─▒lar─▒ keser (┬º13 E2)
    private readonly ResiliencePipeline _circuitBreaker;

    // E4: Tur ├Ârt├╝┼ƒme kilidi ÔÇö her OnSnapshot ├ºa─ƒr─▒s─▒ s─▒rayla ├ºal─▒┼ƒ─▒r,
    // async void ├╝st ├╝ste binmesini engeller (┬º13 E4)
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    public PredictionService(
        LiveDataService live,
        PredictionApiClient predApi,
        ILogger<PredictionService> log,
        IOptions<PredictionApiOptions> predOpt)
    {
        _predApi = predApi;
        _log = log;
        _predOpt = predOpt.Value;

        // E2: Circuit breaker ÔÇö Polly v8 ResiliencePipeline (┬º13 E2)
        // Servis down bilinince pe┼ƒ pe┼ƒe ├ºa─ƒr─▒lar─▒ h─▒zl─▒ ba┼ƒar─▒s─▒z olur, 10 sn beklemesin
        _circuitBreaker = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = (float)_predOpt.FailureThreshold / (_predOpt.FailureThreshold + 1),
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = _predOpt.FailureThreshold,
                BreakDuration = TimeSpan.FromSeconds(_predOpt.RecoverySeconds)
            })
            .Build();

        // Canli akisa baglan
        live.SnapshotReceived += OnSnapshot;
        _log.LogInformation(
            "PredictionService baslatildi ÔÇö sliding window: {WindowSize}, UseModelService: {UseModel}",
            WindowSize, _predOpt.UseModelService);
    }

    /// <summary>
    /// Her snapshot'ta ├ºa─ƒr─▒l─▒r. ┬º12 S1: art─▒k PARALEL ÔÇö t├╝m varl─▒klar─▒
    /// ayn─▒ anda i┼ƒler, s─▒ral─▒ await yok. (┬º12 D-S1)
    /// E4: SemaphoreSlim ile ├╝st ├╝ste binme engellenir.
    /// </summary>
    private async void OnSnapshot(IReadOnlyDictionary<string, TelemetryBase> live)
    {
        // E4: Tur ├Ârt├╝┼ƒme kilidi ÔÇö ├Ânceki tur bitmeden yenisi ba┼ƒlamaz
        await _snapshotLock.WaitAsync();
        try
        {
            // ┬º12 D-S1: her varl─▒k i├ºin tahmin g├Ârevini olu┼ƒtur, sonra paralel ├ºal─▒┼ƒt─▒r
            var tasks = new List<Task<PredictionResult>>();

            foreach (var (id, tel) in live)
            {
                PushHistory(id, tel);
                tasks.Add(ProcessOne(id, tel));
            }

            // T├╝m g├Ârevleri paralel bekle ÔÇö birinin hatas─▒ di─ƒerlerini durdurmaz
            var results = await Task.WhenAll(tasks);
            foreach (var res in results)
            {
                PredictionReady?.Invoke(res);
            }
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    /// <summary>
    /// Tek bir varl─▒─ƒ─▒ i┼ƒle: kompres├Âr + UseModelService=true ÔåÆ model servisi,
    /// aksi halde kural tabanl─▒ sa─ƒl─▒k. (┬º12 D-S1)
    /// </summary>
    private async Task<PredictionResult> ProcessOne(string id, TelemetryBase tel)
    {
        try
        {
            PredictionResult res = _predOpt.UseModelService && tel is CompressorTelemetry c
                ? await PredictCompressor(id, c)   // F2: model servisine git
                : RuleBasedHealth(id, tel);         // F1: her tip yerel kural

            return res;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Varlik isleme hatasi: {EntityId}", id);
            // Hata durumunda kural tabanl─▒ fallback
            return RuleBasedHealth(id, tel);
        }
    }

    /// <summary>
    /// Kompres├Âr i├ºin sliding window g├Ânder + tahmin servisi ├ºa─ƒr─▒s─▒.
    /// E2: Circuit breaker ile korumal─▒ ÔÇö servis down ise h─▒zl─▒ ba┼ƒar─▒s─▒z (┬º13 E2).
    /// </summary>
    private async Task<PredictionResult> PredictCompressor(string id, CompressorTelemetry c)
    {
        try
        {
            // E2: Circuit breaker ile ├ºa─ƒr─▒ yap ÔÇö servis down ise h─▒zl─▒ ba┼ƒar─▒s─▒z olsun
            PredictionResult result = await _circuitBreaker.ExecuteAsync(async ct =>
            {
                var history = _history.TryGetValue(id, out var q) ? q : Enumerable.Empty<TelemetryBase>();
                return await _predApi.PredictAsync(id, c, history, ct);
            });

            result.Source = "model";
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tahmin servisi cagrilamadi, kural tabani kullaniliyor: {EntityId}", id);
            return RuleBasedHealth(id, c);
        }
    }

    /// <summary>
    /// Kural tabanl─▒ sa─ƒl─▒k skoru ÔÇö model olmayan varl─▒klar i├ºin (┬º4.3).
    /// Kompres├Âr, segment, UGS, border, fsru, lng_terminal i├ºin e┼ƒik kurallar─▒.
    /// D-S4: standby durumunu ayr─▒ status ile d├Ând├╝r, health_score=0.0 g├Âsterme.
    /// </summary>
    private PredictionResult RuleBasedHealth(string id, TelemetryBase tel)
    {
        // Kural: kompres├Âr
        if (tel is CompressorTelemetry c)
        {
            bool anomaly = false;
            string faultMode = "";
            double healthScore = 1.0;

            // Durus durumu ÔÇö D-S4: standby ayr─▒ status, health_score null de─ƒil ama UI gri boyar
            if (c.Status == "standby" || c.ShaftRpm < 100)
            {
                return new PredictionResult
                {
                    EntityId = id,
                    Rul = 0,
                    HealthScore = 1.0, // standby = sa─ƒl─▒kl─▒ duru┼ƒ, health bozulmamal─▒
                    Anomaly = false,
                    FaultMode = "",
                    Source = "rule",
                    Status = "standby" // D-S4: UI griye boyar
                };
            }

            // Vibrasyon kural─▒ (┬º8, ┬º4.3)
            if (c.VibrationDe > 11)
            {
                anomaly = true;
                faultMode = "high_vibration_de";
                healthScore = 0.2;
            }
            else if (c.VibrationDe > 7)
            {
                anomaly = true;
                faultMode = "elevated_vibration_de";
                healthScore = 0.6;
            }

            // Rulman s─▒cakl─▒─ƒ─▒ kural─▒ (┬º8, ┬º4.3)
            if (c.BearingTemp1C > 90 || c.BearingTemp2C > 90)
            {
                anomaly = true;
                faultMode = "high_bearing_temp";
                healthScore = 0.1;
            }
            else if (c.BearingTemp1C > 80 || c.BearingTemp2C > 80)
            {
                anomaly = true;
                faultMode = "elevated_bearing_temp";
                healthScore = 0.5;
            }

            // Surge margin kural─▒ (┬º8)
            if (c.SurgeMarginPct < 15)
            {
                anomaly = true;
                faultMode = "low_surge_margin";
                healthScore = Math.Min(healthScore, 0.4);
            }

            // Fault durumu ÔÇö kritik kural ihlali varsa
            string status = anomaly && healthScore < 0.3 ? "fault" : "running";

            return new PredictionResult
            {
                EntityId = id,
                Rul = anomaly ? -1 : 9999, // anlams─▒z ÔÇö kural tabanl─▒ RUL vermez
                HealthScore = healthScore,
                Anomaly = anomaly,
                FaultMode = faultMode,
                Source = "rule",
                Status = status
            };
        }

        // Kural: segment (┬º4.3)
        if (tel is SegmentTelemetry s)
        {
            bool anomaly = false;
            string faultMode = "";
            double healthScore = 1.0;

            // Ka├ºak i┼ƒareti ÔÇö mass imbalance > 2% (┬º4.3, ┬º8)
            if (Math.Abs(s.MassImbalancePct) > 2.0)
            {
                anomaly = true;
                faultMode = "mass_imbalance_high";
                healthScore = 0.3;
            }

            // Koruma gerilimi d├╝┼ƒ├╝k (┬º4.3)
            if (s.CathodicProtectionV < 0.85)
            {
                anomaly = true;
                faultMode = "cathodic_protection_weak";
                healthScore = Math.Min(healthScore, 0.5);
            }

            return new PredictionResult
            {
                EntityId = id,
                Rul = -1,
                HealthScore = healthScore,
                Anomaly = anomaly,
                FaultMode = faultMode,
                Source = "rule",
                Status = anomaly ? "fault" : "running"
            };
        }

        // Kural: UGS (┬º4.3)
        if (tel is StorageTelemetry u)
        {
            bool anomaly = u.StorageLevelPct < 10;
            return new PredictionResult
            {
                EntityId = id,
                Rul = -1,
                HealthScore = anomaly ? 0.3 : 1.0,
                Anomaly = anomaly,
                FaultMode = anomaly ? "low_storage_level" : "",
                Source = "rule",
                Status = anomaly ? "fault" : "running"
            };
        }

        // Kural: border_entry (┬º4.3)
        if (tel is BorderTelemetry b)
        {
            bool anomaly = b.PressureBar < 80 || b.PressureBar > 130;
            return new PredictionResult
            {
                EntityId = id,
                Rul = -1,
                HealthScore = anomaly ? 0.4 : 1.0,
                Anomaly = anomaly,
                FaultMode = anomaly ? "pressure_out_of_band" : "",
                Source = "rule",
                Status = anomaly ? "fault" : "running"
            };
        }

        // Kural: fsru
        if (tel is FsruTelemetry f)
        {
            bool anomaly = f.StorageLevelPct < 15;
            return new PredictionResult
            {
                EntityId = id,
                Rul = -1,
                HealthScore = anomaly ? 0.3 : 1.0,
                Anomaly = anomaly,
                FaultMode = anomaly ? "low_storage_level" : "",
                Source = "rule",
                Status = anomaly ? "fault" : "running"
            };
        }

        // Kural: lng_terminal
        if (tel is LngTerminalTelemetry l)
        {
            bool anomaly = l.StorageLevelPct < 15;
            return new PredictionResult
            {
                EntityId = id,
                Rul = -1,
                HealthScore = anomaly ? 0.3 : 1.0,
                Anomaly = anomaly,
                FaultMode = anomaly ? "low_storage_level" : "",
                Source = "rule",
                Status = anomaly ? "fault" : "running"
            };
        }

        // Bilinmeyen tip ÔÇö default
        return new PredictionResult
        {
            EntityId = id,
            Rul = -1,
            HealthScore = 0.5,
            Anomaly = false,
            FaultMode = "unknown_type",
            Source = "rule",
            Status = "running"
        };
    }

    /// <summary>
    /// Sliding window'a yeni okuma ekle, pencere d─▒┼ƒ─▒na kalanlar─▒ sil (┬º4.1).
    /// </summary>
    private void PushHistory(string entityId, TelemetryBase tel)
    {
        var q = _history.GetOrAdd(entityId, _ => new Queue<TelemetryBase>());
        q.Enqueue(tel);
        while (q.Count > WindowSize)
            q.Dequeue();
    }
}
