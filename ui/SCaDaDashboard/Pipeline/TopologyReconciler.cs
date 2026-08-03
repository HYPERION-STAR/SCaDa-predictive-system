using System.Globalization;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Gelen topolojideki uç tutarsızlıklarını onarır. Bir segmentin ucundaki düğüm,
/// düğüm listesinde YOKSA (ör. TürkAkım hattı <c>SEG-TURKSTREAM-SINOP</c>,
/// <c>from_node = BORDER-RUSYA-01</c>'i referanslar ama düğüm feed'inde bu istasyon
/// <c>BORDER-TURKEY-RUS-01</c> id'siyle gelir), o uç sessizce düşürülmez:
///   1) Segmentin güzergâh geometrisinin ilgili ucundaki koordinatta ZATEN bir düğüm
///      varsa (aynı fiziksel nokta — yalnız id yazımı farklı), segment o gerçek düğüme
///      YENİDEN BAĞLANIR (düğümün adı/telemetrisi/sağlığı korunur, kopya oluşmaz).
///   2) Öyle bir düğüm yoksa, koordinattan yeni bir düğüm SENTEZLENİR.
///   3) Geometri yoksa (koordinat türetilemiyorsa) segment gerçekten kopuk sayılıp atılır.
/// Böylece Rusya'dan gelen gibi sınır kaynaklı hatlar haritada çizilir.
///
/// Üç yükleyici de (canlı API, tam snapshot, yerel topoloji) eskiden aynı
/// <c>RemoveAll</c> filtresini kullanıyordu; artık hepsi buradan geçer (tek kural).
/// </summary>
public static class TopologyReconciler
{
    // Aynı fiziksel noktayı kabul etme yarıçapı (derece; ~0.02° ≈ 2 km).
    private const double CoincidentDeg = 0.02;

    /// <summary>
    /// Segment uçlarındaki eksik düğümleri var olan düğüme yeniden bağlar ya da
    /// geometriden sentezler; onarılamayan (geometrisiz) kopuk segmentleri atar.
    /// <paramref name="nodes"/> ve <paramref name="segments"/> yerinde değiştirilir.
    /// </summary>
    public static void EnsureSegmentEndpoints(List<PNode> nodes, List<PSegment> segments)
    {
        var byId = new Dictionary<string, PNode>(StringComparer.Ordinal);
        foreach (var n in nodes) byId[n.Id] = n;

        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            string from = ResolveEndpoint(nodes, byId, s.From, s, atStart: true);
            string to = ResolveEndpoint(nodes, byId, s.To, s, atStart: false);
            if (from != s.From || to != s.To)
                segments[i] = s with { From = from, To = to };
        }

        // Hâlâ ucu bilinmeyen (onarılamayan) segmentleri at.
        segments.RemoveAll(s => !byId.ContainsKey(s.From) || !byId.ContainsKey(s.To));
    }

    // Uç düğümü çözer: varsa aynen, yoksa koordinatça eşleşen gerçek düğüme
    // yeniden bağlar, o da yoksa sentezler. Kullanılacak düğüm id'sini döndürür
    // (çözülemezse orijinal id — segment sonradan elenir).
    private static string ResolveEndpoint(
        List<PNode> nodes, Dictionary<string, PNode> byId, string id, PSegment seg, bool atStart)
    {
        if (id.Length == 0 || byId.ContainsKey(id)) return id;
        if (seg.Geometry is not { Count: >= 1 } geo) return id; // koordinat yok → onarılamaz

        var pt = atStart ? geo[0] : geo[^1]; // from_node = ilk nokta, to_node = son nokta

        // 1) Aynı koordinatta gerçek bir düğüm var mı? → ona yeniden bağla (kopya yok).
        foreach (var n in nodes)
            if (Math.Abs(n.Lat - pt.Lat) <= CoincidentDeg && Math.Abs(n.Lon - pt.Lon) <= CoincidentDeg)
                return n.Id;

        // 2) Yoksa: koordinattan yeni düğüm sentezle.
        string type = ApiSnapshotLoader.InferType(id);
        var node = new PNode(id, FriendlyName(id, type), type, pt.Lat, pt.Lon, false);
        nodes.Add(node);
        byId[id] = node;
        return id;
    }

    // Id'den okunur ad üretir: "BORDER-RUSYA-01" → "Rusya Sınır Girişi".
    private static string FriendlyName(string id, string type)
    {
        var parts = id.Split('-');
        string token = parts.Length >= 2 ? parts[1] : id;
        string pretty = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(token.ToLowerInvariant());
        return type == "BORDER" ? $"{pretty} Sınır Girişi" : pretty;
    }
}
