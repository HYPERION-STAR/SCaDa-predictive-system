using System.IO;
using System.Text.Json;

namespace SCaDaDashboard.Pipeline;

/// <summary>Snapshot'taki bir ekipman ünitesi (istasyon drill-in kartları için).</summary>
public sealed record SnapshotUnit(string Id, string Model, string UnitType);

/// <summary>Snapshot dosyasından okunan tam ağ + telemetri (statik anlık görüntü).</summary>
public sealed class SnapshotData
{
    public required IReadOnlyList<PNode> Nodes { get; init; }
    public required IReadOnlyList<PSegment> Segments { get; init; }

    /// <summary>Normalize anahtar (küçük harf, '-'→'_') → sayısal sensör değerleri.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Telemetry { get; init; }

    /// <summary>İstasyon düğüm id → o istasyondaki üniteler.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<SnapshotUnit>> StationUnits { get; init; }
}

/// <summary>
/// Tam ağ snapshot JSON'unu okur: kök altındaki her bölge nesnesinin
/// (nodes dizisi içerenler) nodes/segments/equipment/telemetry'sini toplar.
/// Düğüm ve segmentler id ile tekilleştirilir; telemetri anahtarları
/// normalize edilir (küçük harf, '-'→'_') — dosyada id yazımı tutarsız
/// olabildiğinden eşleşme her iki tarafta da normalize anahtarla yapılır.
/// </summary>
public static class SnapshotLoader
{
    /// <summary>"CS-Bursa_U1" → "cs_bursa_u1" (telemetri anahtar kuralı).</summary>
    public static string Norm(string id) => id.ToLowerInvariant().Replace('-', '_');

    public static SnapshotData Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var rawNodes = new Dictionary<string, (string Name, string Type, double Lat, double Lon, string Region)>();
        var segments = new List<PSegment>();
        var segSeen = new HashSet<string>();
        var telemetry = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        var stationUnits = new Dictionary<string, List<SnapshotUnit>>();

        foreach (var region in doc.RootElement.EnumerateObject())
        {
            // meta / regions listesi gibi bölge olmayan anahtarları atla.
            if (region.Value.ValueKind != JsonValueKind.Object) continue;
            if (!region.Value.TryGetProperty("nodes", out var nodeArr)
                || nodeArr.ValueKind != JsonValueKind.Array) continue;

            foreach (var n in nodeArr.EnumerateArray())
            {
                string id = n.GetProperty("node_id").GetString() ?? "";
                if (id.Length == 0 || rawNodes.ContainsKey(id)) continue; // tekilleştir
                string type = n.GetProperty("node_type").GetString() ?? "JUNCTION";
                if (type == "UGS") type = "STORAGE"; // harita kontrolünün tanıdığı tip
                rawNodes[id] = (
                    n.GetProperty("name").GetString() ?? id,
                    type,
                    n.GetProperty("lat").GetDouble(),
                    n.GetProperty("lon").GetDouble(),
                    region.Name); // snapshot bölge anahtarı (ör. "ic_anadolu")
            }

            if (region.Value.TryGetProperty("segments", out var segArr)
                && segArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in segArr.EnumerateArray())
                {
                    string id = s.GetProperty("segment_id").GetString() ?? "";
                    if (id.Length == 0 || !segSeen.Add(id)) continue; // tekilleştir

                    List<GeoPoint>? geometry = null;
                    if (s.TryGetProperty("geometry_polyline", out var poly)
                        && poly.ValueKind == JsonValueKind.Array && poly.GetArrayLength() >= 2)
                    {
                        geometry = new List<GeoPoint>();
                        foreach (var pt in poly.EnumerateArray())
                            geometry.Add(new GeoPoint(pt[0].GetDouble(), pt[1].GetDouble()));
                    }

                    segments.Add(new PSegment(
                        id,
                        s.GetProperty("from_node").GetString() ?? "",
                        s.GetProperty("to_node").GetString() ?? "",
                        s.TryGetProperty("product", out var pr) ? pr.GetString() ?? "gas" : "gas",
                        s.TryGetProperty("max_capacity_mcm_day", out var mc) ? mc.GetDouble() : 0,
                        geometry));
                }
            }

            if (region.Value.TryGetProperty("equipment", out var eqArr)
                && eqArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var eq in eqArr.EnumerateArray())
                {
                    string unitId = eq.GetProperty("unit_id").GetString() ?? "";
                    string station = eq.GetProperty("station_node_id").GetString() ?? "";
                    if (unitId.Length == 0 || station.Length == 0) continue;
                    if (!stationUnits.TryGetValue(station, out var list))
                        stationUnits[station] = list = new List<SnapshotUnit>();
                    if (list.All(u => u.Id != unitId))
                        list.Add(new SnapshotUnit(
                            unitId,
                            eq.TryGetProperty("model", out var mo) ? mo.GetString() ?? "" : "",
                            eq.TryGetProperty("unit_type", out var ut) ? ut.GetString() ?? "" : ""));
                }
            }

            if (region.Value.TryGetProperty("telemetry", out var telObj)
                && telObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in telObj.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                    var vals = new Dictionary<string, double>();
                    foreach (var p in entry.Value.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.Number)
                            vals[p.Name] = p.Value.GetDouble();

                    // Hem dosyadaki anahtar hem entity_id ile eriş (yazım farkları için).
                    telemetry[Norm(entry.Name)] = vals;
                    if (entry.Value.TryGetProperty("entity_id", out var eid)
                        && eid.ValueKind == JsonValueKind.String)
                        telemetry[Norm(eid.GetString()!)] = vals;
                }
            }
        }

        // İstasyon = ünitesi olan CS düğümü (sağlık proxy'si üniteden türetilir).
        var nodes = new List<PNode>(rawNodes.Count);
        foreach (var (id, r) in rawNodes)
            nodes.Add(new PNode(id, r.Name, r.Type, r.Lat, r.Lon,
                r.Type == "CS" && stationUnits.ContainsKey(id), r.Region));

        // Segmentin ucundaki düğüm dosyada yoksa (ör. Rusya sınır girişi) güzergâh
        // geometrisinden sentezle; aksi halde hat düşürülüp haritada görünmez olur.
        TopologyReconciler.EnsureSegmentEndpoints(nodes, segments);

        return new SnapshotData
        {
            Nodes = nodes,
            Segments = segments,
            Telemetry = telemetry,
            StationUnits = stationUnits.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<SnapshotUnit>)kv.Value),
        };
    }
}
