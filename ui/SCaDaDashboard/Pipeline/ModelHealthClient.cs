using System.Net.Http;
using System.Text.Json;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Kişi 1'in tahmin servisinden (/hub_state) model sağlık + RUL'unu periyodik çeker.
/// Harita, titreşim proxy'si yerine bunu kullanır (bkz. SnapshotSource.TryModelHealth).
/// Servis kapalıysa sözlük son iyi hâlde kalır / boştur ve kaynak otomatik proxy'ye
/// düşer — UI her koşulda çalışır. Birim (unit) ID ile anahtarlanır; /hub_state
/// düğüm id'leri canlı entity_id'lerdir (ör. CS-KIRKLARELI-U1), topoloji ünite
/// id'leriyle aynı ad uzayı.
/// </summary>
public sealed class ModelHealthClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly string _url;
    private readonly System.Threading.Timer _timer;

    // Atomik referans takası (fetch arka planda, okuma UI thread'inde).
    private volatile Dictionary<string, (double Health, double Rul)> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public ModelHealthClient(string? url = null, int pollSeconds = 5)
    {
        _url = url
            ?? Environment.GetEnvironmentVariable("MODEL_HUB_URL")
            ?? "http://localhost:8010/hub_state";
        _timer = new System.Threading.Timer(
            _ => _ = FetchAsync(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(pollSeconds));
    }

    /// <summary>Ünitenin model sağlığı (%0-100) + RUL'u; yoksa false.</summary>
    public bool TryGet(string unitId, out (double Health, double Rul) v) =>
        _map.TryGetValue(unitId, out v);

    private async Task FetchAsync()
    {
        try
        {
            await using var s = await _http.GetStreamAsync(_url);
            var state = await JsonSerializer.DeserializeAsync<HubState>(s,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state?.Nodes == null || state.Nodes.Count == 0) return;

            var m = new Dictionary<string, (double, double)>(
                state.Nodes.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var n in state.Nodes) m[n.Id] = (n.Health, n.Rul);
            _map = m; // atomik referans takası
        }
        catch { /* servis yok → son iyi harita korunur, kaynak proxy'ye düşer */ }
    }

    private sealed class HubState
    {
        public List<HubNode> Nodes { get; set; } = new();
    }

    private sealed class HubNode
    {
        public string Id { get; set; } = "";
        public double Health { get; set; }
        public double Rul { get; set; }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
    }
}
