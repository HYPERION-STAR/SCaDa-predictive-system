namespace SCaDaDashboard.Models;

/// <summary>
/// Bir makinenin sabit kimlik bilgisi (sicil karti benzeri).
/// </summary>
public sealed record MachineDescriptor(string Id, string Name, string Type);

/// <summary>
/// Veri kaynagindan (simulasyon veya ileride MQTT + model) gelen anlik olcum + tahmin.
/// Kisi 1'in model_predictor.py cikti formatiyla birebir ayni alanlar:
///   { rul, health_score, anomaly, is_degraded, anomaly_probability }
/// Ek olarak UI'da gosterilecek 3 temsili sensor: sicaklik, basinc, titresim.
/// </summary>
public readonly record struct MachineSnapshot(
    double Temperature,        // °C
    double Pressure,           // bar
    double Vibration,          // mm/s
    double Rul,                // kalan faydali omur (dongu)
    double HealthScore,        // %0-100
    bool Anomaly,
    bool IsDegraded,
    double AnomalyProbability);
