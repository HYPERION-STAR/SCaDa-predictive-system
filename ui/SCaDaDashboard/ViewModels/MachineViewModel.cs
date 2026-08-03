using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SCaDaDashboard.Models;

namespace SCaDaDashboard.ViewModels;

/// <summary>Bir makinenin ekranda gorunen tum durumu (kart).</summary>
public sealed class MachineViewModel : INotifyPropertyChanged
{
    private const int HistoryLength = 60;   // grafiklerde tutulan nokta sayisi

    private readonly List<double> _tempHist = new();
    private readonly List<double> _presHist = new();
    private readonly List<double> _vibHist = new();

    public MachineViewModel(MachineDescriptor desc)
    {
        Id = desc.Id;
        Name = desc.Name;
        Type = desc.Type;
    }

    public string Id { get; }
    public string Name { get; }
    public string Type { get; }

    /// <summary>Kaynak bakim uygulayabiliyor mu? (Snapshot'ta false → buton gizli.)</summary>
    public bool CanMaintain { get; init; } = true;

    /// <summary>Kritik esige yeni girildiginde MainViewModel'i uyarir.</summary>
    public event Action<MachineViewModel>? AlarmRaised;

    /// <summary>Kullanici "Bakim Yap" dediginde tetiklenir.</summary>
    public event Action<MachineViewModel>? MaintenanceRequested;

    public RelayCommand MaintainCommand => new(_ => MaintenanceRequested?.Invoke(this));

    // --- Anlik degerler ---
    private double _temperature;
    public double Temperature { get => _temperature; private set => Set(ref _temperature, value); }

    private double _pressure;
    public double Pressure { get => _pressure; private set => Set(ref _pressure, value); }

    private double _vibration;
    public double Vibration { get => _vibration; private set => Set(ref _vibration, value); }

    private double _rul;
    public double Rul { get => _rul; private set => Set(ref _rul, value); }

    private double _healthScore;
    public double HealthScore { get => _healthScore; private set => Set(ref _healthScore, value); }

    private double _anomalyProbability;
    public double AnomalyProbability { get => _anomalyProbability; private set => Set(ref _anomalyProbability, value); }

    // --- Turetilmis gorsel durum ---
    private string _statusText = "—";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    private bool _isAlarm;
    public bool IsAlarm { get => _isAlarm; private set => Set(ref _isAlarm, value); }

    // --- Grafik verileri (her tik yeni dizi -> MiniChart yeniden cizer) ---
    public IReadOnlyList<double> TemperatureHistory { get; private set; } = Array.Empty<double>();
    public IReadOnlyList<double> PressureHistory { get; private set; } = Array.Empty<double>();
    public IReadOnlyList<double> VibrationHistory { get; private set; } = Array.Empty<double>();

    private int _statusLevel = -1; // 0 saglikli, 1 uyari, 2 riskli, 3 kritik

    public void Update(MachineSnapshot s)
    {
        Temperature = s.Temperature;
        Pressure = s.Pressure;
        Vibration = s.Vibration;
        Rul = s.Rul;
        HealthScore = s.HealthScore;
        AnomalyProbability = s.AnomalyProbability;

        Push(_tempHist, s.Temperature);
        Push(_presHist, s.Pressure);
        Push(_vibHist, s.Vibration);
        TemperatureHistory = _tempHist.ToArray();
        PressureHistory = _presHist.ToArray();
        VibrationHistory = _vibHist.ToArray();
        OnPropertyChanged(nameof(TemperatureHistory));
        OnPropertyChanged(nameof(PressureHistory));
        OnPropertyChanged(nameof(VibrationHistory));

        int level = ClassifyLevel(s);
        ApplyStatus(level);

        // Sadece daha iyi durumdan kritige GECISTE alarm uret (surekli spam olmasin).
        if (level == 3 && _statusLevel != 3)
            AlarmRaised?.Invoke(this);

        _statusLevel = level;
    }

    private static int ClassifyLevel(MachineSnapshot s)
    {
        if (s.HealthScore < 20 || s.Anomaly) return 3; // Kritik
        if (s.HealthScore < 40) return 2;              // Riskli
        if (s.HealthScore < 70) return 1;              // Uyari
        return 0;                                      // Saglikli
    }

    private void ApplyStatus(int level)
    {
        switch (level)
        {
            case 3:
                StatusText = "KRİTİK"; StatusBrush = MakeBrush(0xE7, 0x4C, 0x3C); IsAlarm = true; break;
            case 2:
                StatusText = "RİSKLİ"; StatusBrush = MakeBrush(0xE6, 0x7E, 0x22); IsAlarm = false; break;
            case 1:
                StatusText = "UYARI"; StatusBrush = MakeBrush(0xF1, 0xC4, 0x0F); IsAlarm = false; break;
            default:
                StatusText = "SAĞLIKLI"; StatusBrush = MakeBrush(0x2E, 0xCC, 0x71); IsAlarm = false; break;
        }
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > HistoryLength)
            list.RemoveAt(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
