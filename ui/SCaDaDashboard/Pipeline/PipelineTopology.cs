namespace SCaDaDashboard.Pipeline;

/// <summary>Coğrafi nokta (segment güzergâh polyline'ları için).</summary>
public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>Harita düğümü (istasyon / sınır / çıkış / kavşak). Region snapshot bölge anahtarı.</summary>
public sealed record PNode(string Id, string Name, string Type, double Lat, double Lon, bool IsStation, string Region = "");

/// <summary>
/// İki düğüm arası boru parçası (hat). MaxCapacity segment kapasitesi
/// (doluluk oranı hesabı için); Geometry doluysa gerçek güzergâh
/// polyline'ı çizilir, boşsa düğümler arası düz çizgi.
/// </summary>
public sealed record PSegment(
    string Id, string From, string To, string Product,
    double MaxCapacity = 0,
    IReadOnlyList<GeoPoint>? Geometry = null);

/// <summary>
/// Canlı harita için topoloji (Kişi 3 UI tarafında, kendine yeten).
/// Jenerik / bağımsız — gerçek kurum adı içermez. Varsayılan 7 düğümlü
/// MVP ağı; Load() ile tam ağ (ör. snapshot) takas edilebilir.
/// </summary>
public static class PipelineTopology
{
    private static readonly IReadOnlyList<PNode> _defaultNodes = new List<PNode>
    {
        new("N1", "Kuzey Giriş",        "BORDER",   41.9, 28.0, false),
        new("N2", "Trakya KS",          "CS",       41.0, 28.9, true),
        new("N3", "İç Anadolu Kavşağı", "JUNCTION", 39.9, 32.8, false),
        new("N4", "Sivas KS",           "CS",       39.7, 37.0, true),
        new("N5", "Doğu Çıkış",         "OFFTAKE",  39.9, 41.3, false),
        new("N6", "Batı Çıkış",         "OFFTAKE",  38.4, 27.1, false),
        new("N7", "Doğu Giriş",         "BORDER",   39.7, 44.0, false),
        new("N8", "Marmara Deposu",     "STORAGE",  40.5, 27.5, false),
    };

    // MVP kapasiteleri simülatörün akış ölçeğiyle uyumlu (~60 mcm/gün tavan).
    private static readonly IReadOnlyList<PSegment> _defaultSegments = new List<PSegment>
    {
        new("S1", "N1", "N2", "gas", 60),
        new("S2", "N2", "N3", "gas", 60),
        new("S3", "N3", "N6", "gas", 60),
        new("S4", "N3", "N4", "gas", 60),
        new("S5", "N4", "N5", "gas", 60),
        new("S6", "N7", "N4", "gas", 60),
        new("S7", "N2", "N8", "gas", 60),
    };

    private static IReadOnlyList<PNode> _nodes = _defaultNodes;
    private static IReadOnlyList<PSegment> _segments = _defaultSegments;

    public static IReadOnlyList<PNode> Nodes => _nodes;
    public static IReadOnlyList<PSegment> Segments => _segments;

    /// <summary>Topolojiyi değiştirir (ör. snapshot'tan yüklenen tam ağ).</summary>
    public static void Load(IReadOnlyList<PNode> nodes, IReadOnlyList<PSegment> segments)
    {
        _nodes = nodes;
        _segments = segments;
    }

    /// <summary>7 düğümlü MVP ağına geri döner (sim/hub kaynakları için).</summary>
    public static void ResetToDefault()
    {
        _nodes = _defaultNodes;
        _segments = _defaultSegments;
    }

    /// <summary>Id'ye karşılık gelen düğüm; yoksa null (çağıran korur — çökme yerine).</summary>
    public static PNode? NodeById(string id) => _nodes.FirstOrDefault(n => n.Id == id);
}
