using System.Text.Json.Serialization;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Models;

public sealed class RegionData
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("nodes")]
    public List<NodeInfo> Nodes { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<SegmentInfo> Segments { get; set; } = new();

    // telemetri anahtarı (cs_ankara_u1) -> telemetri kaydı (polimorfik)
    [JsonPropertyName("telemetry")]
    public Dictionary<string, TelemetryBase> Telemetry { get; set; } = new();
}
