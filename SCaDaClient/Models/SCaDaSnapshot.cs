using System.Text.Json.Serialization;

namespace SCaDaClient.Models;

/// <summary>
/// A endpoint (/live_data) kök modeli — snapshot dosyasıyla aynı yapı.
/// </summary>
public sealed class SCaDaSnapshot
{
    [JsonPropertyName("meta")]
    public SCaDaMeta Meta { get; set; } = new();

    // bölge_adı -> bölge verisi  (meta hariç tüm anahtarlar)
    [JsonExtensionData]
    public Dictionary<string, RegionData> Regions { get; set; } = new();
}

public sealed class SCaDaMeta
{
    [JsonPropertyName("project")]
    public string Project { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("simulation_cycle")]
    public int SimulationCycle { get; set; }

    [JsonPropertyName("season_profile")]
    public string SeasonProfile { get; set; } = "";

    [JsonPropertyName("ambient_temp_c")]
    public double AmbientTempC { get; set; }

    [JsonPropertyName("total_nodes")]
    public int TotalNodes { get; set; }

    [JsonPropertyName("total_segments")]
    public int TotalSegments { get; set; }

    [JsonPropertyName("total_equipment")]
    public int TotalEquipment { get; set; }

    [JsonPropertyName("total_telemetry_tags")]
    public int TotalTelemetryTags { get; set; }
}
