using System.Text.Json.Serialization;

namespace SCaDaClient.Models;

/// <summary>
/// Tahmin sonucu s├Âzle┼ƒmesi ÔÇö model veya kural tabanl─▒ sa─ƒl─▒k skoru.
/// ┬º6 ENTEGRASYON_TAHMIN_SISTEMI.md ile uyumlu.
/// D-S4: status alan─▒ eklendi (running/standby/fault).
/// </summary>
public sealed class PredictionResult
{
    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("rul")]
    public double Rul { get; set; }

    [JsonPropertyName("health_score")]
    public double HealthScore { get; set; }

    [JsonPropertyName("anomaly")]
    public bool Anomaly { get; set; }

    [JsonPropertyName("fault_mode")]
    public string? FaultMode { get; set; }

    /// <summary>Kaynak: "model" (ML) veya "rule" (e┼ƒik kural)</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = ""; // "model" | "rule"

    /// <summary>Varl─▒k durumu: running / standby / fault (┬º8, D-S4)</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "running";
}
