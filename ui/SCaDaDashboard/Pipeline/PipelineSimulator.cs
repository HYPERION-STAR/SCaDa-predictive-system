using SCaDaDashboard.Models;

namespace SCaDaDashboard.Pipeline;

/// <summary>Bir düğümün anlık durumu (istasyonlar için sağlık/RUL).</summary>
public readonly record struct NodeSnap(double Health, double Rul, bool IsStation);

/// <summary>Bir segmentin anlık durumu (akış + doluluk oranı 0..1 + sızıntı).</summary>
public readonly record struct SegSnap(double FlowMcmDay, double LoadRatio, bool Leak);

/// <summary>İstasyon detay sensörleri (yan panel grafikleri için).</summary>
public readonly record struct StationSensors(double Vibration, double BearingTemp, double DischargePressure);

/// <summary>Bir istasyon ünitesinin sağlık durumu (harita pasta dilimi + kırılım için).</summary>
public readonly record struct UnitHealth(string UnitId, string Name, double Health, bool HasTelemetry);

/// <summary>İstasyon dairesinin görünümü: pasta (ünite başına dilim), ortalama, en kötü.</summary>
public enum HealthDisplayMode { Pie, Average, Worst }

/// <summary>Özet sağlık toplama kuralı (pasta kenar rengi için).</summary>
public enum HealthAggregate { Worst, Average }

/// <summary>
/// Canlı harita veri kaynağı soyutlaması. Bugün SIMULE; ileride gerçek akışa
/// (Kişi 2 MQTT + Kişi 1 model) bağlanacak — harita değişmeden.
/// </summary>
public interface IPipelineSource
{
    void Tick();
    NodeSnap Node(string id);
    SegSnap Segment(string id);
    StationSensors Sensors(string id);
    double Level(string id); // depo doluluk %0-100 (depo degilse 0)

    /// <summary>İstasyonun ünitelerinin tek tek sağlığı (pasta görünümü + kırılım).
    /// İstasyon değilse/ünite yoksa boş liste.</summary>
    IReadOnlyList<UnitHealth> UnitHealths(string id);
}

/// <summary>Ünite sağlıklarını görünüm moduna göre tek sayıya indirger (harita + panel ortak).</summary>
public static class HealthAgg
{
    /// <summary>Telemetrisi olan ünitelerin toplaması; hiçbiri yoksa <paramref name="fallback"/>.</summary>
    public static double Combine(IReadOnlyList<UnitHealth> units, HealthAggregate agg, double fallback)
    {
        double sum = 0, min = double.MaxValue; int n = 0;
        foreach (var u in units)
        {
            if (!u.HasTelemetry) continue;
            sum += u.Health; if (u.Health < min) min = u.Health; n++;
        }
        if (n == 0) return fallback;
        return agg == HealthAggregate.Average ? sum / n : min;
    }
}

/// <summary>
/// Basit, kendine yeten harita simülasyonu. Her istasyon (CS) birkaç ÜNİTE
/// (kompresör) içerir; üniteler zamanla bozulur, 0'a inince otomatik bakımla
/// toparlanır. İstasyonun harita sağlığı = en kötü ünitenin sağlığı.
/// Segment akışları günlük profille dalgalanır.
/// </summary>
public sealed class PipelineSimulator : IPipelineSource
{
    private const double MaxRul = 130.0;

    private sealed class Unit
    {
        public required string Id;
        public required string Name;
        public double Rul;
        public double DegradePerTick;
        public int FailedTicks;
    }

    private readonly Dictionary<string, List<Unit>> _stationUnits = new();
    private readonly Dictionary<string, Unit> _unitById = new();
    private readonly Dictionary<string, double> _segBaseFlow = new();
    private readonly Random _rng = new(42);
    private int _tick;

    public PipelineSimulator()
    {
        // Her istasyonda 2 kompresör ünitesi (topoloji verisi; ileride değişir).
        AddStation("N2", ("N2-U1", "Ünite A", 120, 0.25), ("N2-U2", "Ünite B", 108, 0.22));
        AddStation("N4", ("N4-U1", "Ünite A", 22, 0.32), ("N4-U2", "Ünite B", 70, 0.28));

        _segBaseFlow["S1"] = 55; _segBaseFlow["S2"] = 50; _segBaseFlow["S3"] = 22;
        _segBaseFlow["S4"] = 30; _segBaseFlow["S5"] = 18; _segBaseFlow["S6"] = 20;
        _segBaseFlow["S7"] = 15;
    }

    private void AddStation(string stationId, params (string Id, string Name, double Rul, double Deg)[] units)
    {
        var list = new List<Unit>();
        foreach (var u in units)
        {
            var unit = new Unit { Id = u.Id, Name = u.Name, Rul = u.Rul, DegradePerTick = u.Deg };
            list.Add(unit);
            _unitById[u.Id] = unit;
        }
        _stationUnits[stationId] = list;
    }

    public void Tick()
    {
        _tick++;
        foreach (var u in _unitById.Values)
        {
            if (u.Rul <= 0)
            {
                u.FailedTicks++;
                if (u.FailedTicks >= 6) { u.Rul = MaxRul; u.FailedTicks = 0; }
            }
            else
            {
                double d = 1.0 - u.Rul / MaxRul;
                double jitter = 0.7 + _rng.NextDouble() * 0.6;
                u.Rul = Math.Max(0, u.Rul - u.DegradePerTick * (1 + d) * jitter);
            }
        }
    }

    // En kötü (en düşük RUL) ünite — istasyon sağlığını o belirler.
    private Unit? WorstUnit(string stationId)
    {
        if (!_stationUnits.TryGetValue(stationId, out var list) || list.Count == 0) return null;
        Unit worst = list[0];
        foreach (var u in list) if (u.Rul < worst.Rul) worst = u;
        return worst;
    }

    public NodeSnap Node(string id)
    {
        var w = WorstUnit(id);
        if (w == null) return new NodeSnap(100, MaxRul, false); // sınır/çıkış/kavşak
        double health = Math.Clamp(w.Rul / MaxRul * 100.0, 0, 100);
        return new NodeSnap(Math.Round(health, 1), Math.Round(w.Rul, 1), true);
    }

    public IReadOnlyList<UnitHealth> UnitHealths(string id)
    {
        if (!_stationUnits.TryGetValue(id, out var list) || list.Count == 0)
            return Array.Empty<UnitHealth>();
        var result = new List<UnitHealth>(list.Count);
        foreach (var u in list)
        {
            double health = Math.Clamp(u.Rul / MaxRul * 100.0, 0, 100);
            result.Add(new UnitHealth(u.Id, u.Name, Math.Round(health, 1), true));
        }
        return result;
    }

    public SegSnap Segment(string id)
    {
        double baseFlow = _segBaseFlow.TryGetValue(id, out var b) ? b : 30;
        double hour = _tick % 24;
        double peak = Math.Max(0, Math.Sin(2 * Math.PI * (hour - 6) / 24));
        double flow = baseFlow * (0.75 + 0.35 * peak) + (_rng.NextDouble() - 0.5) * 2;
        double load = Math.Clamp(flow / 60.0, 0, 1);
        // S4 segmentinde periyodik sizinti (kutle dengesizligi) - alarm demosu.
        bool leak = id == "S4" && _tick % 40 >= 26 && _tick % 40 < 36;
        return new SegSnap(Math.Round(flow, 1), load, leak);
    }

    public StationSensors Sensors(string id)
    {
        var w = WorstUnit(id);
        if (w == null) return default;
        double d = Math.Clamp(1.0 - w.Rul / MaxRul, 0, 1);
        return new StationSensors(
            Math.Round(2.0 + d * 7.0 + Noise(0.2), 2),
            Math.Round(70.0 + d * 28.0 + Noise(0.6), 1),
            Math.Round(Math.Max(1.0, 75.0 - d * 10.0 + Noise(0.3)), 2));
    }

    // --- İstasyon üniteleri (tıklayınca açılan makine kartları için) ---------

    public bool IsStation(string id) => _stationUnits.ContainsKey(id);

    public IReadOnlyList<(string Id, string Name)> UnitList(string stationId) =>
        _stationUnits.TryGetValue(stationId, out var list)
            ? list.Select(u => (u.Id, u.Name)).ToList()
            : new List<(string, string)>();

    public MachineSnapshot UnitSnapshot(string unitId)
    {
        if (!_unitById.TryGetValue(unitId, out var u)) return default;
        double rul = Math.Max(0, u.Rul);
        double d = Math.Clamp(1.0 - rul / MaxRul, 0, 1);
        double health = Math.Clamp(rul / MaxRul * 100.0, 0, 100);
        bool degraded = rul < 30;
        double anomalyProb = 1.0 / (1.0 + Math.Exp((rul - 30.0) / 6.0));
        return new MachineSnapshot(
            Temperature: Math.Round(70.0 + d * 28.0 + Noise(0.6), 1),
            Pressure: Math.Round(Math.Max(1.0, 75.0 - d * 10.0 + Noise(0.3)), 2),
            Vibration: Math.Round(2.0 + d * 7.0 + Noise(0.2), 2),
            Rul: Math.Round(rul, 1),
            HealthScore: Math.Round(health, 1),
            Anomaly: health < 20 || degraded,
            IsDegraded: degraded,
            AnomalyProbability: Math.Round(anomalyProb, 3));
    }

    public void MaintainUnit(string unitId)
    {
        if (_unitById.TryGetValue(unitId, out var u)) { u.Rul = MaxRul; u.FailedTicks = 0; }
    }

    // Depo doluluk (enjeksiyon/cekis dalgalanmasi).
    public double Level(string id) =>
        id == "N8" ? Math.Round(55 + 30 * Math.Sin(_tick / 50.0), 0) : 0;

    private double Noise(double a) => (_rng.NextDouble() - 0.5) * 2 * a;
}
