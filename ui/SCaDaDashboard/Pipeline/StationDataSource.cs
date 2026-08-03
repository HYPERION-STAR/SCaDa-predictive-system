using SCaDaDashboard.Models;
using SCaDaDashboard.Services;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Bir istasyonun ünitelerini makine paneli (MainWindow) için IDataSource'a
/// köprüler. Ortak PipelineSimulator'ı kullanır — harita ile aynı sayılar.
/// Simülasyonu harita ilerletir; burada Tick() boştur (çift hız olmasın).
/// </summary>
public sealed class StationDataSource : IDataSource
{
    private readonly PipelineSimulator _sim;
    private readonly List<MachineDescriptor> _machines;

    public StationDataSource(PipelineSimulator sim, string stationId)
    {
        _sim = sim;
        _machines = _sim.UnitList(stationId)
            .Select(u => new MachineDescriptor(u.Id, u.Name, "Kompresör"))
            .ToList();
    }

    public IReadOnlyList<MachineDescriptor> Machines => _machines;

    public void Tick() { /* ortak sim harita tarafından ilerletilir */ }

    public MachineSnapshot GetSnapshot(string machineId) => _sim.UnitSnapshot(machineId);

    public void Maintain(string machineId) => _sim.MaintainUnit(machineId);
}
