using SCaDaDashboard.Models;

namespace SCaDaDashboard.Services;

/// <summary>
/// Dashboard'un veri kaynagi soyutlamasi.
/// Bugun: SimulatorDataSource (sahte ama gercekci veri).
/// Yarin: MqttDataSource (Kisi 2'nin factory/motor/sensor_data akisi + Kisi 1'in ONNX modeli)
/// ayni arayuzu uygulayacak; UI hic degismeden gercek veriye baglanacak.
/// </summary>
public interface IDataSource
{
    /// <summary>Izlenen makinelerin sabit listesi.</summary>
    IReadOnlyList<MachineDescriptor> Machines { get; }

    /// <summary>Simulasyonu/akisi bir adim ilerletir (1 tik = ~1 sn).</summary>
    void Tick();

    /// <summary>Belirtilen makinenin son durumunu dondurur.</summary>
    MachineSnapshot GetSnapshot(string machineId);

    /// <summary>Bakim yap: makineyi sifirdan saglikli duruma dondurur (demo/etkilesim).</summary>
    void Maintain(string machineId);

    /// <summary>
    /// Bu kaynak bakim uygulayabilir mi? Statik kaynaklarda (ör. snapshot)
    /// false — UI "Bakim Yap" butonunu gizler.
    /// </summary>
    bool CanMaintain => true;
}
