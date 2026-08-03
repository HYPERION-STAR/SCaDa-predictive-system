using System.Net.Http.Headers;
using SCaDaClient.Models;
using SCaDaClient.Models.Telemetry;
using SCaDaClient.Parsing;

namespace SCaDaClient.Services;

/// <summary>
/// HttpClient sarmalayıcı — A ve B endpoint'lerini kullanır.
/// IHttpClientFactory ile yönetilir, Polly retry içerir.
/// </summary>
public sealed class SCaDaApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SCaDaJsonParser _parser;
    private bool _disposed;

    public SCaDaApiClient(HttpClient http, SCaDaJsonParser parser)
    {
        _http = http;
        _parser = parser;
    }

    /// <summary>
    /// A endpoint: Tüm ağın anlık görüntüsü (/api/SCaDa/live_data).
    /// Gerçek sunucu düz harita döndürür: { "entity_id": { entity_type, s_*, ... }, ... }
    /// ResponseHeadersRead: büyük gövde bellek dolmadan akıtılır.
    /// </summary>
    public async Task<LiveDataSnapshot> GetLiveDataAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SCaDaApiClient));

        using var resp = await _http.GetAsync(
            "/api/SCaDa/live_data",
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var rawJson = await resp.Content.ReadAsStringAsync(ct);
        var entities = _parser.ParseLiveData(rawJson);
        if (entities.Count == 0)
            throw new InvalidDataException("/live_data returned an empty JSON object.");

        var capturedAt = entities.Values
            .Select(entity => entity.Timestamp)
            .Where(timestamp => timestamp != default)
            .DefaultIfEmpty(DateTime.UtcNow)
            .Max();

        return new LiveDataSnapshot(entities, rawJson, capturedAt);
    }

    /// <summary>
    /// B endpoint: Tek düğümün detayı (/api/SCaDa/node/{nodeId}).
    /// </summary>
    public async Task<NodeDetail> GetNodeAsync(string nodeId, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SCaDaApiClient));

        using var resp = await _http.GetAsync($"/api/SCaDa/node/{Uri.EscapeDataString(nodeId)}", ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await _parser.ParseNodeDetailAsync(stream, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
