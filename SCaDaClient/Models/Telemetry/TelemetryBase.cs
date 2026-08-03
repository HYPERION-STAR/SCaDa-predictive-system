using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCaDaClient.Models.Telemetry;

/// <summary>
/// Ortak telemetri taban sınıfı.
/// TelemetryConverter, entity_type'a göre doğru somut tipe çözer.
/// </summary>
[JsonConverter(typeof(Parsing.TelemetryConverter))]
public abstract class TelemetryBase
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("entity_type")]
    public string EntityType { get; set; } = "";

    [JsonPropertyName("quality")]
    public string Quality { get; set; } = "GOOD";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>Ek alanlar (entity_type'a özgü, alt sınıflarda doldurulur).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Alt sınıflar için deşifreleme desteği.</summary>
    protected void Deconstruct(out string entityId, out DateTime timestamp)
    {
        entityId = EntityId;
        timestamp = Timestamp;
    }
}
