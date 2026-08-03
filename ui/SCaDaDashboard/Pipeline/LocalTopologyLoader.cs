using System.IO;
using System.Text.Json;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Yerel ÇIKARILMIŞ topolojiyi diskten okur: <c>SCaDa_nodes.json</c> +
/// <c>SCaDa_segments.json</c> — canlı API'nin düz /nodes,/segments biçimi
/// (aynı alanlar). Amaç: gerçek boru hattı haritasıyla (btaş.jpg) istasyon/konum
/// eşleştirmesini ÇEVRİMDIŞI yapabilmek; sunucuya bağlanmadan düzeltilmiş topoloji.
///
/// SADECE TOPOLOJİ: çıkarımda düğümlerin live_equipment/telemetry alanları boş,
/// segmentlerde canlı akış yok. Bu yüzden ünite ve telemetri YÜKLENMEZ — istasyonlar
/// id önekinden (CS) işaretlenir, sağlık/akış nötr görünür. Zengin (telemetrili)
/// çevrimdışı görünüm isteniyorsa "Snapshot" modu (SCaDa_live_snapshot.json) kullanılır.
/// </summary>
public static class LocalTopologyLoader
{
    public static SnapshotData Load(string nodesPath, string segsPath)
    {
        using var nodesDoc = JsonDocument.Parse(File.ReadAllText(nodesPath));
        using var segsDoc = JsonDocument.Parse(File.ReadAllText(segsPath));

        // --- Düğümler ---
        var nodes = new List<PNode>();
        foreach (var n in nodesDoc.RootElement.EnumerateArray())
        {
            string id = Str(n, "node_id");
            if (id.Length == 0) continue;
            string type = ApiSnapshotLoader.InferType(id); // ortak tip çıkarımı

            nodes.Add(new PNode(
                id,
                Str(n, "name", id),
                type,
                Num(n, "lat"),
                Num(n, "lon"),
                type == "CS", // ünite yok → istasyonu tipe göre işaretle
                Str(n, "region")));
        }

        // --- Segmentler ---
        var segments = new List<PSegment>();
        var seen = new HashSet<string>();
        foreach (var s in segsDoc.RootElement.EnumerateArray())
        {
            string id = Str(s, "segment_id");
            if (id.Length == 0 || !seen.Add(id)) continue;

            List<GeoPoint>? geometry = null;
            if (s.TryGetProperty("geometry_polyline", out var poly)
                && poly.ValueKind == JsonValueKind.Array && poly.GetArrayLength() >= 2)
            {
                geometry = new List<GeoPoint>();
                foreach (var pt in poly.EnumerateArray())
                    geometry.Add(new GeoPoint(pt[0].GetDouble(), pt[1].GetDouble())); // [lat, lon]
            }

            segments.Add(new PSegment(
                id,
                Str(s, "from_node"),
                Str(s, "to_node"),
                Str(s, "product", "gas"),
                Num(s, "max_capacity_mcm_day"),
                geometry));
        }

        // Segmentin ucundaki düğüm dosyada yoksa (ör. Rusya sınır girişi) güzergâh
        // geometrisinden sentezle; aksi halde hat düşürülüp haritada görünmez olur.
        TopologyReconciler.EnsureSegmentEndpoints(nodes, segments);

        return new SnapshotData
        {
            Nodes = nodes,
            Segments = segments,
            Telemetry = new Dictionary<string, IReadOnlyDictionary<string, double>>(), // yok
            StationUnits = new Dictionary<string, IReadOnlyList<SnapshotUnit>>(),        // yok
        };
    }

    private static string Str(JsonElement obj, string prop, string fallback = "") =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback : fallback;

    private static double Num(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
