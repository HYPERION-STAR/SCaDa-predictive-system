using System.Text.Json.Serialization;

namespace SCaDaClient.Models;

/// <summary>
/// Model servisi /hub_state yanıtı — kompresör düğümleri + boru segmentleri.
/// </summary>
public sealed class AiHubState
{
    [JsonPropertyName("nodes")]
    public List<AiHubNode> Nodes { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<AiHubSegment> Segments { get; set; } = new();

    /// <summary>Modelin bildirdiği zaman damgası (örn. "2026-07-23T15:12:38")</summary>
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Tek kompresörün model çıktısı (sağlık / kalan ömür + girdi sensörleri).</summary>
public sealed class AiHubNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("health")]
    public double Health { get; set; }

    [JsonPropertyName("rul")]
    public double Rul { get; set; }

    [JsonPropertyName("vibration")]
    public double Vibration { get; set; }

    [JsonPropertyName("bearingTemp")]
    public double BearingTemp { get; set; }

    [JsonPropertyName("dischargePressure")]
    public double DischargePressure { get; set; }
}

/// <summary>Tek boru segmentinin akış / yük durumu ve sızıntı bayrağı.</summary>
public sealed class AiHubSegment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("flow")]
    public double Flow { get; set; }

    [JsonPropertyName("load")]
    public double Load { get; set; }

    [JsonPropertyName("leak")]
    public bool Leak { get; set; }
}

/// <summary>
/// Tek bir /hub_state çekiminin ham gövdesi + toplama meta verisi.
/// ai_respond tablosuna yazılacak her şeyi taşır.
/// </summary>
public sealed record AiHubFetch(
    AiHubState State,
    string RawJson,
    string SourceUrl,
    int HttpStatus,
    long LatencyMs,
    DateTime FetchedAt);

/// <summary>ai_respond'a yazma sonucu — log satırı için özet.</summary>
public sealed record AiRespondSaveResult(
    ulong RespondId,
    int NodeCount,
    int SegmentCount,
    int CriticalNodeCount,
    int LeakSegmentCount,
    bool Skipped);

/// <summary>
/// Karşı bilgisayara POST edilecek zarf: modelin ham gövdesi (payload) +
/// izlenebilirlik için DB meta verisi. Alan adları snake_case — karşı taraf
/// Python/JS ile okuyacak.
/// </summary>
public sealed record AiRespondEnvelope(
    [property: JsonPropertyName("respond_id")] ulong RespondId,
    [property: JsonPropertyName("fetched_at")] DateTime FetchedAt,
    [property: JsonPropertyName("source_url")] string SourceUrl,
    [property: JsonPropertyName("model_updated_at")] DateTime? ModelUpdatedAt,
    [property: JsonPropertyName("node_count")] int NodeCount,
    [property: JsonPropertyName("segment_count")] int SegmentCount,
    [property: JsonPropertyName("critical_node_count")] int CriticalNodeCount,
    [property: JsonPropertyName("leak_segment_count")] int LeakSegmentCount,
    [property: JsonPropertyName("min_health")] decimal? MinHealth,
    [property: JsonPropertyName("min_rul")] decimal? MinRul,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement Payload);
