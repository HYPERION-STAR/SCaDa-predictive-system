using System.Net.Http;
using System.Text.Json;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Canlı haritayı API Hub'dan (Kişi 2 - FastAPI) besleyen veri kaynağı.
/// DB'ye veya MQTT'ye DOKUNMAZ; sadece Hub'ın birleştirilmiş (aggregated)
/// temiz JSON'ını HTTP GET ile tüketir (proje planı, Kişi 3 görevi).
///
/// Beklenen sözleşme — GET {baseUrl} şunu döndürmeli:
/// {
///   "nodes":    [ { "id":"N2", "health":91, "rul":118,
///                   "vibration":2.7, "bearingTemp":72, "dischargePressure":74 } ],
///   "segments": [ { "id":"S1", "flow":41, "load":0.7 } ]
/// }
/// (Hub narrow telemetriyi bu forma indirger; UI ham narrow'ı toplamaz.)
///
/// Hub erişilemezse son iyi değerler korunur; hiç veri yoksa güvenli
/// varsayılan döner. Böylece Hub olmadan da UI çökmez.
/// </summary>
public sealed class HttpPipelineSource : IPipelineSource
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly string _url;

    private Dictionary<string, NodeSnap> _nodes = new();
    private Dictionary<string, SegSnap> _segs = new();
    private Dictionary<string, StationSensors> _sensors = new();

    public HttpPipelineSource(string baseUrl) => _url = baseUrl;

    // UI her saniye çağırır; Hub'dan çekişi tetikler (fire-and-forget).
    public void Tick() => _ = FetchAsync();

    private async Task FetchAsync()
    {
        try
        {
            await using var s = await _http.GetStreamAsync(_url);
            var state = await JsonSerializer.DeserializeAsync<HubState>(s,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state == null) return;

            var nodes = new Dictionary<string, NodeSnap>();
            var sensors = new Dictionary<string, StationSensors>();
            foreach (var n in state.Nodes)
            {
                nodes[n.Id] = new NodeSnap(n.Health, n.Rul, true);
                sensors[n.Id] = new StationSensors(n.Vibration, n.BearingTemp, n.DischargePressure);
            }
            var segs = new Dictionary<string, SegSnap>();
            foreach (var seg in state.Segments)
                segs[seg.Id] = new SegSnap(seg.Flow, Math.Clamp(seg.Load, 0, 1), seg.Leak);

            _nodes = nodes; _segs = segs; _sensors = sensors; // atomik referans degisimi
        }
        catch { /* Hub yoksa son iyi degerleri koru */ }
    }

    public NodeSnap Node(string id) => _nodes.TryGetValue(id, out var v) ? v : new NodeSnap(100, 130, false);
    public SegSnap Segment(string id) => _segs.TryGetValue(id, out var v) ? v : new SegSnap(0, 0, false);
    public StationSensors Sensors(string id) => _sensors.TryGetValue(id, out var v) ? v : default;
    public double Level(string id) => 0; // Hub'dan depo doluluk ileride eklenecek

    // Hub düğüm bazlı sağlık verir (ünite kırılımı yok) → boş; harita tek renk dolguya düşer.
    public IReadOnlyList<UnitHealth> UnitHealths(string id) => Array.Empty<UnitHealth>();

    private sealed class HubState
    {
        public List<HubNode> Nodes { get; set; } = new();
        public List<HubSeg> Segments { get; set; } = new();
    }

    private sealed class HubNode
    {
        public string Id { get; set; } = "";
        public double Health { get; set; }
        public double Rul { get; set; }
        public double Vibration { get; set; }
        public double BearingTemp { get; set; }
        public double DischargePressure { get; set; }
    }

    private sealed class HubSeg
    {
        public string Id { get; set; } = "";
        public double Flow { get; set; }
        public double Load { get; set; }
        public bool Leak { get; set; }
    }
}
