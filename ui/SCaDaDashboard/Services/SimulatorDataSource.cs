using SCaDaDashboard.Models;

namespace SCaDaDashboard.Services;

/// <summary>
/// SIMULE veri kaynagi. Kisi 2'nin MQTT akisi ve Kisi 1'in canli modeli hazir olana
/// kadar dashboard'u gercekci, bozulan sensor verisiyle besler.
///
/// Mantik Kisi 1'in modelini taklit eder:
///   - Her makinenin bir RUL'i (kalan omur) vardir, zamanla azalir.
///   - Bozulma arttikca sicaklik/titresim yukselir, basinc duser (C-MAPSS deseni).
///   - health_score = clamp(RUL / MAX_RUL * 100)   (bkz. model_predictor.py)
///   - is_degraded  = RUL < 30
///   - anomaly      = health &lt; 20 veya is_degraded
///   - RUL 0'a inince makine "arizalanir", birkac tik sonra otomatik bakimla toparlanir.
/// </summary>
public sealed class SimulatorDataSource : IDataSource
{
    private const double MaxRul = 130.0;   // model_predictor.py: MAX_RUL

    private sealed class Sim
    {
        public required MachineDescriptor Desc;
        public double Rul;
        public double DegradePerTick;
        public double BaseTemp;      // °C  (saglikli taban)
        public double BasePressure;  // bar
        public double BaseVibration; // mm/s
        public int FailedTicks;      // ariza sonrasi bakim geri sayimi
    }

    private readonly List<Sim> _sims;
    private readonly Dictionary<string, Sim> _byId;
    private readonly Dictionary<string, MachineSnapshot> _last = new();
    private readonly Random _rng = new(42);

    public SimulatorDataSource()
    {
        // Farkli baslangic RUL'leri: panelde ayni anda saglikli/uyari/riskli/kritik gorunsun.
        _sims = new List<Sim>
        {
            new() { Desc = new("MTR-01", "Ana Tahrik Motoru", "Motor"),      Rul = 128, DegradePerTick = 0.18, BaseTemp = 58, BasePressure = 6.2, BaseVibration = 1.1 },
            new() { Desc = new("PMP-02", "Soğutma Pompası",    "Pompa"),      Rul = 96,  DegradePerTick = 0.22, BaseTemp = 46, BasePressure = 8.0, BaseVibration = 0.9 },
            new() { Desc = new("CNV-03", "Konveyör Hattı",     "Konveyör"),   Rul = 57,  DegradePerTick = 0.28, BaseTemp = 40, BasePressure = 4.5, BaseVibration = 1.4 },
            new() { Desc = new("FAN-04", "Egzoz Fanı",         "Fan"),        Rul = 33,  DegradePerTick = 0.20, BaseTemp = 52, BasePressure = 3.2, BaseVibration = 1.7 },
            new() { Desc = new("CMP-05", "Hava Kompresörü",    "Kompresör"),  Rul = 21,  DegradePerTick = 0.16, BaseTemp = 63, BasePressure = 9.5, BaseVibration = 2.0 },
        };
        _byId = _sims.ToDictionary(s => s.Desc.Id);

        // Ilk anlik goruntuleri uret.
        foreach (var s in _sims)
            _last[s.Desc.Id] = Compute(s);
    }

    public IReadOnlyList<MachineDescriptor> Machines => _sims.Select(s => s.Desc).ToList();

    public void Tick()
    {
        foreach (var s in _sims)
        {
            if (s.Rul <= 0)
            {
                // Ariza durumu: birkac saniye kritik kalsin, sonra otomatik bakim.
                s.FailedTicks++;
                if (s.FailedTicks >= 6)
                {
                    s.Rul = MaxRul;         // bakim tamamlandi
                    s.FailedTicks = 0;
                }
            }
            else
            {
                // Sona yaklastikca bozulma hizlanir (exponential son - C-MAPSS gibi).
                double d = 1.0 - s.Rul / MaxRul;
                double accel = 1.0 + d;                      // 1.0 -> 2.0
                double jitter = 0.7 + _rng.NextDouble() * 0.6; // 0.7 - 1.3
                s.Rul -= s.DegradePerTick * accel * jitter;
                if (s.Rul < 0) s.Rul = 0;
            }

            _last[s.Desc.Id] = Compute(s);
        }
    }

    public MachineSnapshot GetSnapshot(string machineId) => _last[machineId];

    public void Maintain(string machineId)
    {
        if (_byId.TryGetValue(machineId, out var s))
        {
            s.Rul = MaxRul;
            s.FailedTicks = 0;
            _last[machineId] = Compute(s);
        }
    }

    /// <summary>Bir makinenin RUL'unden sensor degerleri + model ciktilarini uretir.</summary>
    private MachineSnapshot Compute(Sim s)
    {
        double rul = Math.Max(0, s.Rul);
        double d = Math.Clamp(1.0 - rul / MaxRul, 0, 1);   // 0 saglikli -> 1 arizali

        double temp = s.BaseTemp + d * 45 + Noise(1.5);
        double vib = s.BaseVibration + d * 7 + Noise(0.25);
        double pres = Math.Max(0.1, s.BasePressure - d * 1.8 + Noise(0.12));

        double health = Math.Clamp(rul / MaxRul * 100.0, 0, 100);
        bool isDegraded = rul < 30;
        // Lojistik olasilik: RUL=30'da ~0.5, dustukce 1'e yaklasir.
        double anomalyProb = 1.0 / (1.0 + Math.Exp((rul - 30.0) / 6.0));
        bool anomaly = health < 20 || isDegraded;

        return new MachineSnapshot(
            Temperature: Math.Round(temp, 1),
            Pressure: Math.Round(pres, 2),
            Vibration: Math.Round(vib, 2),
            Rul: Math.Round(rul, 1),
            HealthScore: Math.Round(health, 1),
            Anomaly: anomaly,
            IsDegraded: isDegraded,
            AnomalyProbability: Math.Round(anomalyProb, 3));
    }

    private double Noise(double amplitude) => (_rng.NextDouble() - 0.5) * 2 * amplitude;
}
