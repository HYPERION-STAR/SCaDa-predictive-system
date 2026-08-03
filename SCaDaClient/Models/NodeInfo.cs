using System.Text.Json.Serialization;

namespace SCaDaClient.Models;

public sealed class NodeInfo
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("node_type")]
    public string NodeType { get; set; } = ""; // CS, PS, UGS...

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("elevation_m")]
    public double ElevationM { get; set; }

    [JsonPropertyName("pipeline_ids")]
    public List<string> PipelineIds { get; set; } = new();

    [JsonPropertyName("design_pressure_bar")]
    public double DesignPressureBar { get; set; }

    [JsonPropertyName("capacity_mcm_day")]
    public double CapacityMcmDay { get; set; }

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("equipment_count")]
    public int EquipmentCount { get; set; }
}

public sealed class SegmentInfo
{
    [JsonPropertyName("segment_id")]
    public string SegmentId { get; set; } = "";

    [JsonPropertyName("from_node")]
    public string FromNode { get; set; } = "";

    [JsonPropertyName("to_node")]
    public string ToNode { get; set; } = "";

    [JsonPropertyName("length_km")]
    public double LengthKm { get; set; }

    [JsonPropertyName("diameter_in")]
    public double DiameterIn { get; set; }

    [JsonPropertyName("material")]
    public string Material { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [JsonPropertyName("commissioning_date")]
    public string CommissioningDate { get; set; } = "";
}
