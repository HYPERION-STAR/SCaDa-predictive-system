using SCaDaDashboard.Models;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Snapshot telemetrisinden beslenen harita kaynağı. Snapshot'ta sağlık/RUL yok
/// (onlar AI'ın çıktısı, DB predictions tablosuyla gelecek) — o yüzden
/// istasyon sağlığı en kötü ünitenin titreşiminden PROXY olarak türetilir:
/// 2 mm/s → %100, 9 mm/s → %0. Statik dosyada Tick() no-op; canlı kaynak
/// (<see cref="LiveSnapshotSource"/>) Tick()'i override edip telemetriyi tazeler.
/// Topoloji her iki durumda da sabittir; yalnızca telemetri değişir.
/// </summary>
public class SnapshotSource : IPipelineSource
{
    private const double VibHealthy = 2.0;   // mm/s → %100
    private const double VibDead = 9.0;      // mm/s → %0
    // Sızıntı eşiği (kütle dengesizliği %). Normal bant tavanı ~2.5 (metadata);
    // 1.5 poll başına sahte sızıntı üretiyordu, bu yüzden gürültünün üstünde ama
    // gerçek dengesizliği yakalayacak şekilde 2.35'e sabitlendi.
    private const double LeakImbalancePct = 2.35;
    private const double LeakClearPct = 2.0;       // histerezis: altına inince sayac sıfırlanır
    private const int LeakPollsRequired = 3;         // canlı: aynı segmentte ardışık poll (≈15 sn @5s)
    private const double MaxRul = 130.0;     // simülatörle aynı RUL ölçeği

    private readonly SnapshotData _data;
    private readonly Dictionary<string, double> _segCapacity = new();

    // Telemetri değiştirilebilir: statik dosyada sabit, canlı kaynakta her tick yenilenir.
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> _telemetry;

    // Varlık başına telemetri kalitesi (canlı kaynakta sunucudan; dosyada boş).
    private IReadOnlyDictionary<string, string> _quality = new Dictionary<string, string>();

    // Canlı poll gürültüsü: tek tur eşik aşımı sızıntı sayılmaz (sunucu rastgele salınım).
    private bool _leakFilterLive;
    private readonly Dictionary<string, int> _leakStreak = new();
    private readonly HashSet<string> _leakActive = new(StringComparer.OrdinalIgnoreCase);

    public SnapshotSource(SnapshotData data)
    {
        _data = data;
        _telemetry = data.Telemetry;
        foreach (var s in data.Segments) _segCapacity[s.Id] = s.MaxCapacity;
        RefreshLeakFlags();
    }

    public virtual void Tick() { } // statik anlık görüntü — zaman ilerlemez

    /// <summary>
    /// Ünitenin MODEL sağlığı (%0-100) + RUL'u. Base'de yok → false (statik/dosya
    /// kaynağı titreşim proxy'sini kullanır). Canlı kaynak (LiveSnapshotSource)
    /// bunu Kişi 1'in tahmin servisinden (/hub_state) doldurur; dolarsa harita
    /// proxy yerine gerçek model çıktısını gösterir, servis kapalıysa proxy'ye düşer.
    /// </summary>
    protected virtual bool TryModelHealth(string unitId, out double health, out double rul)
    {
        health = 0; rul = 0; return false;
    }

    /// <summary>Telemetriyi + kaliteyi atomik olarak değiştirir (canlı poll her turda çağırır).</summary>
    protected void SetTelemetry(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> telemetry,
        IReadOnlyDictionary<string, string> quality)
    {
        if (!_leakFilterLive)
        {
            _leakFilterLive = true;
            _leakStreak.Clear();
            _leakActive.Clear();
        }
        _telemetry = telemetry;
        _quality = quality;
        RefreshLeakFlags();
    }

    /// <summary>
    /// Az sayıda varlığın telemetri+kalitesini mevcut haritanın üzerine bindirir —
    /// canlı B ucundan (/node/{id}) gelen taze düğüm telemetrisi için (bir sonraki
    /// poll'a kadar tazedir). Boş bindirme yok sayılır. Referans takası atomiktir;
    /// eşzamanlı bir poll ile nadir yarış kendi kendini bir sonraki turda düzeltir.
    /// </summary>
    protected void MergeTelemetry(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> overlay,
        IReadOnlyDictionary<string, string> quality)
    {
        if (overlay.Count == 0) return;
        var mergedTel = new Dictionary<string, IReadOnlyDictionary<string, double>>(_telemetry);
        foreach (var kv in overlay) mergedTel[kv.Key] = kv.Value;
        var mergedQ = new Dictionary<string, string>(_quality);
        foreach (var kv in quality) mergedQ[kv.Key] = kv.Value;
        _telemetry = mergedTel;
        _quality = mergedQ;
        RefreshLeakFlags();
    }

    /// <summary>
    /// Sızıntı bayrağı: dosyada anlık eşik; canlıda aynı segmentte <see cref="LeakPollsRequired"/>
    /// ardışık poll eşik üstü kalınca latch (rastgele tek-tur spike'ları eler).
    /// </summary>
    private void RefreshLeakFlags()
    {
        if (!_leakFilterLive)
        {
            _leakActive.Clear();
            foreach (var s in _data.Segments)
            {
                if (SegmentImbalance(s.Id) > LeakImbalancePct)
                    _leakActive.Add(s.Id);
            }
            return;
        }

        foreach (var s in _data.Segments)
        {
            string id = s.Id;
            double abs = SegmentImbalance(id);
            if (abs > LeakImbalancePct)
            {
                int streak = _leakStreak.GetValueOrDefault(id) + 1;
                _leakStreak[id] = streak;
                if (streak >= LeakPollsRequired)
                    _leakActive.Add(id);
            }
            else if (abs < LeakClearPct)
            {
                _leakStreak.Remove(id);
                _leakActive.Remove(id);
            }
        }
    }

    private double SegmentImbalance(string segmentId)
    {
        var t = Telem(segmentId);
        return t == null ? 0 : Math.Abs(t.GetValueOrDefault("s_mass_imbalance_pct"));
    }

    private IReadOnlyDictionary<string, double>? Telem(string id) =>
        _telemetry.TryGetValue(SnapshotLoader.Norm(id), out var t) ? t : null;

    /// <summary>
    /// İstasyonun telemetri kalitesi: ünitelerinden HERHANGİ biri GOOD değilse o
    /// şüpheli durumu (WARNING/BAD/…) döndürür — kalite entity başına bir sinyaldir,
    /// sağlık proxy'sinden (titreşim sıralaması) bağımsızdır. Hepsi GOOD ise "GOOD",
    /// hiç kalite bilgisi yoksa "" (dosya yolunda boş). Canlı kaynakta sunucudan gelir.
    /// </summary>
    public string StationDataQuality(string stationId)
    {
        if (!_data.StationUnits.TryGetValue(stationId, out var units)) return "";
        string seen = "";
        foreach (var u in units)
        {
            if (!_quality.TryGetValue(SnapshotLoader.Norm(u.Id), out var q) || q.Length == 0) continue;
            if (!q.Equals("GOOD", StringComparison.OrdinalIgnoreCase)) return q; // ilk şüpheli → uyar
            seen = q; // en az bir GOOD görüldü
        }
        return seen;
    }

    // Titreşimden proxy sağlık (2 mm/s → %100, 9 mm/s → %0).
    private static double VibHealth(double vib) =>
        Math.Clamp((VibDead - vib) / (VibDead - VibHealthy) * 100.0, 0, 100);

    // İstasyonun en kötü (en yüksek titreşimli) ünitesi + telemetrisi.
    private (string Id, IReadOnlyDictionary<string, double> Sensors)? WorstUnitWithId(string stationId)
    {
        if (!_data.StationUnits.TryGetValue(stationId, out var units)) return null;
        (string, IReadOnlyDictionary<string, double>)? worst = null;
        double worstVib = -1;
        foreach (var u in units)
        {
            var t = Telem(u.Id);
            if (t == null) continue;
            double vib = t.GetValueOrDefault("s_7_vibration_de_mm_s");
            if (vib > worstVib) { worstVib = vib; worst = (u.Id, t); }
        }
        return worst;
    }

    private IReadOnlyDictionary<string, double>? WorstUnit(string stationId) =>
        WorstUnitWithId(stationId)?.Sensors;

    /// <summary>En kötü ünitenin tüm sensörleri (detay paneli, 21 sensör).</summary>
    public (string UnitId, IReadOnlyDictionary<string, double> Sensors)? WorstUnitSensors(string stationId) =>
        WorstUnitWithId(stationId);

    public NodeSnap Node(string id)
    {
        var w = WorstUnitWithId(id);
        if (w == null) return new NodeSnap(100, MaxRul, false); // sınır/çıkış/kavşak/depo

        // Model varsa (tahmin servisi) onu göster; yoksa titreşim proxy'sine düş.
        if (TryModelHealth(w.Value.Id, out var mh, out var mr))
            return new NodeSnap(Math.Round(mh, 1), Math.Round(mr, 0), true);

        double health = VibHealth(w.Value.Sensors.GetValueOrDefault("s_7_vibration_de_mm_s"));
        // RUL proxy: model servisi kapalıyken sağlıkla orantılı gösterim.
        return new NodeSnap(Math.Round(health, 1), Math.Round(health / 100.0 * MaxRul, 0), true);
    }

    public IReadOnlyList<UnitHealth> UnitHealths(string id)
    {
        if (!_data.StationUnits.TryGetValue(id, out var units) || units.Count == 0)
            return Array.Empty<UnitHealth>();
        var result = new List<UnitHealth>(units.Count);
        foreach (var u in units)
        {
            var t = Telem(u.Id);
            // Telemetrisi yoksa "bilinmeyen" dilim (haritada gri); sağlık 100 tutulur ki
            // toplama (min/ortalama) yalnız gerçek verili üniteleri saysın.
            bool hasTelem = t != null;
            bool hasModel = TryModelHealth(u.Id, out var mh, out _);
            double health = hasModel ? mh                                            // model (canlı tahmin)
                          : hasTelem ? VibHealth(t!.GetValueOrDefault("s_7_vibration_de_mm_s"))
                          : 100;
            result.Add(new UnitHealth(u.Id, u.Id, Math.Round(health, 1), hasTelem || hasModel));
        }
        return result;
    }

    // --- İstasyon üniteleri (haritadan açılan makine kartları için) ---------

    /// <summary>İstasyonun ünite listesi (drill-in kartları).</summary>
    public IReadOnlyList<SnapshotUnit> UnitList(string stationId) =>
        _data.StationUnits.TryGetValue(stationId, out var units)
            ? units : Array.Empty<SnapshotUnit>();

    /// <summary>Tek ünitenin makine kartı görünümü (proxy sağlık, statik).</summary>
    public MachineSnapshot UnitSnapshot(string unitId)
    {
        var t = Telem(unitId);
        if (t == null) return default;
        double vib = t.GetValueOrDefault("s_7_vibration_de_mm_s");
        // Model varsa sağlık+RUL oradan; yoksa titreşim proxy'si.
        double health, rul;
        if (TryModelHealth(unitId, out var mh, out var mr)) { health = mh; rul = mr; }
        else { health = VibHealth(vib); rul = health / 100.0 * MaxRul; }
        // Anomali olasılığı: sağlık üzerinden lojistik eğri (ölçekten bağımsız, %30 eşik).
        double anomalyProb = 1.0 / (1.0 + Math.Exp((health - 30.0) / 6.0));
        return new MachineSnapshot(
            Temperature: Math.Round(t.GetValueOrDefault("s_10_bearing_temp_1_c"), 1),
            Pressure: Math.Round(t.GetValueOrDefault("s_2_discharge_pressure_bar"), 2),
            Vibration: Math.Round(vib, 2),
            Rul: Math.Round(rul, 1),
            HealthScore: Math.Round(health, 1),
            Anomaly: health < 20,
            IsDegraded: health < 40,
            AnomalyProbability: Math.Round(anomalyProb, 3));
    }

    public SegSnap Segment(string id)
    {
        var t = Telem(id);
        if (t == null) return new SegSnap(0, 0, false);

        double flowM3H = t.GetValueOrDefault("s_flow_m3_h");
        double flowMcmDay = flowM3H * 24.0 / 1e6; // m³/saat → mcm/gün

        // Doluluk: snapshot kapasiteleri bin m³/gün ölçeğiyle uyumlu
        // (ör. 420000 m³/sa ≈ 10080 vs kapasite 15000 → ~0.67).
        double cap = _segCapacity.GetValueOrDefault(id);
        double load = cap > 0 ? Math.Clamp(flowM3H * 24.0 / 1000.0 / cap, 0, 1) : 0;

        bool leak = _leakActive.Contains(id);
        return new SegSnap(Math.Round(flowMcmDay, 1), load, leak);
    }

    public StationSensors Sensors(string id)
    {
        var w = WorstUnit(id);
        if (w == null) return default;
        return new StationSensors(
            w.GetValueOrDefault("s_7_vibration_de_mm_s"),
            w.GetValueOrDefault("s_10_bearing_temp_1_c"),
            w.GetValueOrDefault("s_2_discharge_pressure_bar"));
    }

    // Depo doluluk yüzdesi. Gaz UGS/LNG/FSRU → s_storage_level_pct; ham petrol
    // deposu (oil_storage) farklı ada sahip → s_tank_level_pct. İkisini de dene.
    public double Level(string id)
    {
        var t = Telem(id);
        if (t == null) return 0;
        return t.TryGetValue("s_storage_level_pct", out var v) ? v
             : t.GetValueOrDefault("s_tank_level_pct");
    }
}
