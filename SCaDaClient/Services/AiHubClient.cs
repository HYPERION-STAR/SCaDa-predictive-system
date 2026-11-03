using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SCaDaClient.Models;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// Model servisinin /hub_state ucunu çeker. Ham gövdeyi de döndürür —
/// ai_respond tablosuna olduğu gibi yazılacak (§ai_respond.sql tasarım notu).
/// </summary>
public sealed class AiHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly AiHubOptions _opt;

    public AiHubClient(HttpClient http, IOptions<AiHubOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public async Task<AiHubFetch> GetHubStateAsync(CancellationToken ct = default)
    {
        var sourceUrl = new Uri(new Uri(_opt.BaseUrl), "/hub_state").ToString();
        var started = Stopwatch.GetTimestamp();

        using var resp = await _http.GetAsync("/hub_state", ct);
        var rawJson = await resp.Content.ReadAsStringAsync(ct);
        var latencyMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        resp.EnsureSuccessStatusCode();

        var state = JsonSerializer.Deserialize<AiHubState>(rawJson, JsonOptions)
            ?? throw new InvalidDataException("/hub_state boş gövde döndürdü.");

        if (state.Nodes.Count == 0 && state.Segments.Count == 0)
            throw new InvalidDataException("/hub_state ne düğüm ne segment içeriyor.");

        return new AiHubFetch(
            state,
            rawJson,
            sourceUrl,
            (int)resp.StatusCode,
            latencyMs,
            DateTime.Now);
    }
}
