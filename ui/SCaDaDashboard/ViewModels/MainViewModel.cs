using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using SCaDaDashboard.Services;

namespace SCaDaDashboard.ViewModels;

/// <summary>
/// Ana pencere modeli. Veri kaynagini 1 sn'de bir yoklar, makine kartlarini
/// ve uyari panelini gunceller, ozet sayaclari hesaplar.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IDataSource _dataSource;
    private readonly AlertService _alerts;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<MachineViewModel> Machines { get; } = new();
    public ObservableCollection<AlertItem> Alerts { get; } = new();

    public MainViewModel(IDataSource dataSource, AlertService alerts, string sourceLabel = "SİMÜLASYON")
    {
        _dataSource = dataSource;
        _alerts = alerts;
        SourceLabel = sourceLabel;

        foreach (var desc in _dataSource.Machines)
        {
            var vm = new MachineViewModel(desc) { CanMaintain = dataSource.CanMaintain };
            vm.AlarmRaised += OnAlarmRaised;
            vm.MaintenanceRequested += OnMaintenanceRequested;
            vm.Update(_dataSource.GetSnapshot(desc.Id));
            Machines.Add(vm);
        }
        RecalculateSummary();

        AddAlert("Bilgi", "SYS", $"Dashboard başlatıldı. Veri kaynağı: {sourceLabel}.");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        _dataSource.Tick();
        foreach (var vm in Machines)
            vm.Update(_dataSource.GetSnapshot(vm.Id));
        RecalculateSummary();
        Clock = DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");
    }

    private void OnAlarmRaised(MachineViewModel vm)
    {
        var msg = $"Sağlık %{vm.HealthScore:0}, kalan ömür {vm.Rul:0} döngü. Bakım gerekli!";
        AddAlert("Kritik", vm.Id, msg);
        _alerts.SendAlert(vm.Id, vm.Name, msg);   // E-posta/SMS simulasyonu (logs/alerts.log)
    }

    private void OnMaintenanceRequested(MachineViewModel vm)
    {
        if (!_dataSource.CanMaintain) return; // statik kaynak: sahte "bakim yapildi" uretme
        _dataSource.Maintain(vm.Id);
        vm.Update(_dataSource.GetSnapshot(vm.Id));
        RecalculateSummary();
        AddAlert("Bilgi", vm.Id, "Bakım yapıldı, makine sağlıklı duruma döndü.");
    }

    private void AddAlert(string severity, string machineId, string message)
    {
        Alerts.Insert(0, new AlertItem
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            MachineId = machineId,
            Message = message,
            Severity = severity
        });
        while (Alerts.Count > 100)
            Alerts.RemoveAt(Alerts.Count - 1);
    }

    private void RecalculateSummary()
    {
        int total = Machines.Count, critical = 0, healthy = 0;
        foreach (var m in Machines)
        {
            if (m.StatusText == "KRİTİK") critical++;
            else if (m.StatusText == "SAĞLIKLI") healthy++;
        }
        TotalCount = total;
        CriticalCount = critical;
        HealthyCount = healthy;
        WarningCount = total - critical - healthy;
    }

    // --- Ozet sayaclar ---
    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => Set(ref _totalCount, value); }

    private int _healthyCount;
    public int HealthyCount { get => _healthyCount; private set => Set(ref _healthyCount, value); }

    private int _warningCount;
    public int WarningCount { get => _warningCount; private set => Set(ref _warningCount, value); }

    private int _criticalCount;
    public int CriticalCount { get => _criticalCount; private set => Set(ref _criticalCount, value); }

    private string _clock = DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");
    public string Clock { get => _clock; private set => Set(ref _clock, value); }

    /// <summary>Üst barda gösterilen veri kaynağı adı (SİMÜLASYON / SNAPSHOT ...).</summary>
    public string SourceLabel { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
