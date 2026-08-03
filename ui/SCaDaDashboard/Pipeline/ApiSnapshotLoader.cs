using System.Net.Http;
using System.Text.Json;
using SCaDaClient.Parsing;
using SCaDaClient.Services;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Topolojiyi (düğüm + segment + istasyon üniteleri) doğrudan canlı API'den kurar —
/// böylece harita, statik snapshot dosyasına bağımlı olmaz ve id'ler /live_data ile
/// birebir hizalıdır (aynı sunucu):
///   GET {base}/api/SCaDa/nodes      → düğümler (node_id, name, region, lat, lon)
///   GET {base}/api/SCaDa/segments   → hatlar (from/to, geometry_polyline, kapasite)
///   GET {base}/api/SCaDa/node/{id}  → istasyon üniteleri — SCaDaClient (Kişi 2) üzerinden
/// /nodes ve /segments için SCaDaClient'ta metod yok (SegmentInfo modeli geometry ve
/// kapasiteyi taşımıyor) → yalnız bu iki uç ham JSON okunur; istemciye eklenirse geçilir.
/// Telemetri buradan doldurulmaz; <see cref="LiveSnapshotSource"/> istemciyle tazeler.
/// API'de node_type alanı yok → düğüm tipi id önekinden çıkarılır.
/// </summary>
public static class ApiSnapshotLoader
{
    public static SnapshotData Load(string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(8),
        };

        var nodesJson = http.GetStringAsync("/api/SCaDa/nodes").GetAwaiter().GetResult();
        var segsJson = http.GetStringAsync("/api/SCaDa/segments").GetAwaiter().GetResult();

        using var nodesDoc = JsonDocument.Parse(nodesJson);
        using var segsDoc = JsonDocument.Parse(segsJson);

        // --- Düğümler ---
        var nodes = new List<PNode>();
        var stationIds = new List<string>();
        foreach (var n in nodesDoc.RootElement.EnumerateArray())
        {
            string id = n.TryGetProperty("node_id", out var nid) ? nid.GetString() ?? "" : "";
            if (id.Length == 0) continue;

            string type = InferType(id);
            if (type == "CS") stationIds.Add(id);

            nodes.Add(new PNode(
                id,
                n.TryGetProperty("name", out var nm) ? nm.GetString() ?? id : id,
                type,
                Num(n, "lat"),
                Num(n, "lon"),
                false, // IsStation üniteler yüklenince belirlenir
                n.TryGetProperty("region", out var rg) ? rg.GetString() ?? "" : ""));
        }

        // --- Segmentler ---
        var segments = new List<PSegment>();
        var segSeen = new HashSet<string>();
        foreach (var s in segsDoc.RootElement.EnumerateArray())
        {
            string id = s.TryGetProperty("segment_id", out var sid) ? sid.GetString() ?? "" : "";
            if (id.Length == 0 || !segSeen.Add(id)) continue;

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
                s.TryGetProperty("from_node", out var fn) ? fn.GetString() ?? "" : "",
                s.TryGetProperty("to_node", out var tn) ? tn.GetString() ?? "" : "",
                s.TryGetProperty("product", out var pr) ? pr.GetString() ?? "gas" : "gas",
                Num(s, "max_capacity_mcm_day"),
                geometry));
        }

        // --- İstasyon üniteleri (yalnız CS düğümleri; SCaDaClient ile, paralel) ---
        // Not: SCaDaApiClient http'yi Dispose eder — bu yüzden using bloğu içinde;
        // topoloji GET'leri yukarıda bitti, çakışma yok.
        using var api = new SCaDaApiClient(http, new SCaDaJsonParser());
        var unitTasks = stationIds.Select(id => FetchUnitsAsync(api, id)).ToArray();
        var unitResults = Task.WhenAll(unitTasks).GetAwaiter().GetResult();

        var stationUnits = new Dictionary<string, IReadOnlyList<SnapshotUnit>>();
        foreach (var (id, units) in unitResults)
            if (units.Count > 0) stationUnits[id] = units;

        // İstasyon = ünitesi olan CS düğümü (sağlık proxy'si üniteden türetilir).
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].Type == "CS" && stationUnits.ContainsKey(nodes[i].Id))
                nodes[i] = nodes[i] with { IsStation = true };

        // Segmentin ucundaki düğüm feed'de gelmemişse (ör. Rusya sınır girişi) güzergâh
        // geometrisinden sentezle; aksi halde hat düşürülüp haritada görünmez olur.
        TopologyReconciler.EnsureSegmentEndpoints(nodes, segments);

        return new SnapshotData
        {
            Nodes = nodes,
            Segments = segments,
            Telemetry = new Dictionary<string, IReadOnlyDictionary<string, double>>(), // ilk tick doldurur
            StationUnits = stationUnits,
        };
    }

    private static async Task<(string Id, IReadOnlyList<SnapshotUnit> Units)> FetchUnitsAsync(
        SCaDaApiClient api, string id)
    {
        try
        {
            // ConfigureAwait(false): yükleyici UI thread'inde .GetResult() ile bloklanıyor;
            // devamları UI SynchronizationContext'ine postlanırsa deadlock olur.
            var detail = await api.GetNodeAsync(id).ConfigureAwait(false);

            var list = new List<SnapshotUnit>();
            foreach (var u in detail.LiveEquipment)
            {
                string uid = u.TryGetProperty("entity_id", out var e) ? e.GetString() ?? "" : "";
                if (uid.Length == 0) continue;
                string utype = u.TryGetProperty("entity_type", out var et) ? et.GetString() ?? "" : "";
                list.Add(new SnapshotUnit(uid, "", utype));
            }
            return (id, list);
        }
        catch { return (id, Array.Empty<SnapshotUnit>()); }
    }

    // API'de node_type yok; tip id önekinden çıkarılır (UGS→STORAGE harita tipiyle uyumlu).
    // internal: LocalTopologyLoader de aynı çıkarımı kullanır (tek kaynak).
    internal static string InferType(string id)
    {
        int dash = id.IndexOf('-');
        string prefix = dash > 0 ? id.Substring(0, dash) : id;
        return prefix switch
        {
            "UGS" => "STORAGE",
            "CS" => "CS",
            "BORDER" => "BORDER",
            "OFFTAKE" => "OFFTAKE",
            "JUNCTION" => "JUNCTION",
            _ => prefix, // LNG / FSRU / TERMINAL vb. — harita jenerik çizer
        };
    }

    private static double Num(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
