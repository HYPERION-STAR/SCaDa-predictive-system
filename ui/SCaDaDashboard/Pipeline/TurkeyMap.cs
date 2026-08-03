using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Gömülü sadeleştirilmiş il sınırlarını (turkey.json) yükler.
/// Her il, dış halkaları [lon, lat] noktalarından oluşan poligonlarla temsil edilir.
/// Kaynak: alpers/Turkey-Maps-GeoJSON (Apache-2.0), sadeleştirilmiş.
/// </summary>
public static class TurkeyMap
{
    public sealed class Province
    {
        public string Name { get; init; } = "";
        /// <summary>Halkalar; her halka [lon, lat] çiftlerinden oluşur.</summary>
        public List<double[][]> Rings { get; init; } = new();
    }

    private static List<Province>? _cache;
    private static bool _tried;

    public static IReadOnlyList<Province> Provinces
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                _cache = Load();
            }
            return _cache ?? new List<Province>();
        }
    }

    private sealed class Dto
    {
        public List<ProvDto> Provinces { get; set; } = new();
    }

    private sealed class ProvDto
    {
        public string Name { get; set; } = "";
        public List<List<List<double>>> Rings { get; set; } = new();
    }

    private static List<Province>? Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("SCaDaDashboard.Pipeline.turkey.json");
            if (stream == null) return null;

            var dto = JsonSerializer.Deserialize<Dto>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return null;

            var list = new List<Province>(dto.Provinces.Count);
            foreach (var p in dto.Provinces)
            {
                var rings = new List<double[][]>(p.Rings.Count);
                foreach (var ring in p.Rings)
                {
                    var pts = new double[ring.Count][];
                    for (int i = 0; i < ring.Count; i++)
                        pts[i] = new[] { ring[i][0], ring[i][1] }; // [lon, lat]
                    rings.Add(pts);
                }
                list.Add(new Province { Name = p.Name, Rings = rings });
            }
            return list;
        }
        catch
        {
            return null; // veri yüklenemezse harita otomatik yerleşime düşer
        }
    }
}
