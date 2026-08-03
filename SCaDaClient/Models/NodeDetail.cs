using System.Text.Json;
using System.Text.Json.Serialization;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Models;

/// <summary>
/// B endpoint (/api/SCaDa/node/{id}) kök modeli — düz yapı (§11.3 D2).
/// Gerçek sunucu: { node_id, name, region, health_state, telemetry, live_equipment }
/// [JsonExtensionData] ile bilinmeyen alanlar yutulur — sunucu alan eklerse istemci kırılmaz.
/// </summary>
public sealed class NodeDetail
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("health_state")]
    public string HealthState { get; set; } = "";

    // düğüme bağlı ekipman telemetrileri (polimorfik — aynı converter)
    [JsonPropertyName("telemetry")]
    public Dictionary<string, TelemetryBase> Telemetry { get; set; } = new();

    // düğüme bağlı ekipman listesi (gövdesi netleşince tipli DTO'ya çevrilebilir)
    [JsonPropertyName("live_equipment")]
    public List<JsonElement> LiveEquipment { get; set; } = new();

    // bilinmeyen alanlar için yedek
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}
