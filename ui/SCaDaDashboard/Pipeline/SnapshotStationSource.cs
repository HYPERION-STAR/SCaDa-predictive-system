using SCaDaDashboard.Models;
using SCaDaDashboard.Services;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Snapshot'taki bir istasyonun ünitelerini makine paneli (MainWindow) için
/// IDataSource'a köprüler. Statik anlık görüntü: Tick() ve Maintain() boştur
/// (bakım simülasyonu yok — gerçek veri DB predictions ile gelecek).
/// </summary>
public sealed class SnapshotStationSource : IDataSource
{
    private readonly SnapshotSource _source;
    private readonly List<MachineDescriptor> _machines;

    public SnapshotStationSource(SnapshotSource source, string stationId)
    {
        _source = source;
        _machines = source.UnitList(stationId)
            .Select(u => new MachineDescriptor(
                u.Id,
                u.Model.Length > 0 ? u.Model : u.Id,
                TypeName(u.UnitType)))
            .ToList();
    }

    private static string TypeName(string unitType) => unitType switch
    {
        "compressor" => "Kompresör",
        "pump" => "Pompa",
        _ => unitType.Length > 0 ? unitType : "Ünite",
    };

    public IReadOnlyList<MachineDescriptor> Machines => _machines;

    public void Tick() { /* statik anlık görüntü */ }

    public MachineSnapshot GetSnapshot(string machineId) => _source.UnitSnapshot(machineId);

    public void Maintain(string machineId) { /* snapshot'ta bakım yok */ }

    // Statik veri — bakım uygulanamaz; UI butonu gizler.
    public bool CanMaintain => false;
}
