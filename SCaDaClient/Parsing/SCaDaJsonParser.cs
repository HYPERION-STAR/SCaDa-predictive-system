using System.Text.Json;
using System.Text.Json.Serialization;
using SCaDaClient.Models;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Parsing;

/// <summary>
/// Ana JSON ayıklayıcı.
/// - /live_data → düz harita (Dictionary&lt;string, TelemetryBase&gt;) — §11.2 D1
/// - /node/{id} → düz NodeDetail (§11.3 D2)
/// - TelemetryConverter'ı options'a ekler — entity_type polimorfik çözücü (§4.2)
/// </summary>
public sealed class SCaDaJsonParser
{
    private readonly JsonSerializerOptions _opts;

    public SCaDaJsonParser()
    {
        _opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new TelemetryConverter() }
        };
    }

    /// <summary>
    /// A endpoint (/api/SCaDa/live_data) yanıtını parse eder.
    /// Gerçek sunucu düz harita döndürür: { "entity_id": { entity_type, s_*, ... }, ... }
    /// TelemetryConverter entity_type'a göre doğru DTO'yu seçer.
    /// </summary>
    public async Task<Dictionary<string, TelemetryBase>> ParseLiveDataAsync(
        Stream stream, CancellationToken ct = default)
    {
        var map = await JsonSerializer.DeserializeAsync<Dictionary<string, TelemetryBase>>(
            stream, _opts, ct);
        return map ?? new();
    }

    public Dictionary<string, TelemetryBase> ParseLiveData(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, TelemetryBase>>(json, _opts)
            ?? new();
    }

    /// <summary>
    /// B endpoint (/api/SCaDa/node/{id}) yanıtını parse eder.
    /// </summary>
    public async Task<NodeDetail> ParseNodeDetailAsync(Stream stream, CancellationToken ct = default)
    {
        return (await JsonSerializer.DeserializeAsync<NodeDetail>(stream, _opts, ct))!;
    }
}
