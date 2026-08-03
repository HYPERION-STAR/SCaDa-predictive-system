using SCaDaClient.Models;

namespace SCaDaClient.Services;

/// <summary>
/// Kullanıcı haritada bir düğüme tıkladığında çağrılır.
/// Tek düğüm çağrısı — talep üzerine çalışır.
/// </summary>
public sealed class NodeDetailClient
{
    private readonly SCaDaApiClient _api;

    public NodeDetailClient(SCaDaApiClient api) => _api = api;

    public Task<NodeDetail> LoadAsync(string nodeId, CancellationToken ct = default)
        => _api.GetNodeAsync(nodeId, ct);
}
