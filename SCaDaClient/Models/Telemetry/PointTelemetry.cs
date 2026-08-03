using System.Text.Json.Serialization;

namespace SCaDaClient.Models.Telemetry;

/// <summary>
/// OFFTAKE / JUNCTION nokta telemetrisi (§11.4 D3).
/// entity_type="segment" ama s_* alanları yok — flow/pressure/temperature taşıyor.
/// Daha temiz çözüm: Kişi 2/4'ten offtake/junction için ayrı entity_type istemek.
/// </summary>
public sealed class PointTelemetry : TelemetryBase
{
    [JsonPropertyName("flow_m3_h")]
    public double FlowM3H { get; set; }

    [JsonPropertyName("pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("temperature_c")]
    public double TemperatureC { get; set; }

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}
