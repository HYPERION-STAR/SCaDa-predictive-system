using System.Text.Json;
using System.Text.Json.Serialization;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Parsing;

/// <summary>
/// Telemetri converter — entity_type'a göre doğru DTO'yu seçer.
/// Sonsuz döngüyü önlemek için kendi kendini iç options'tan kaldırır.
/// </summary>
public sealed class TelemetryConverter : JsonConverter<TelemetryBase>
{
    public override TelemetryBase Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Nesneyi bir kez oku, entity_type'ı bul, doğru tipe yeniden çöz
        using var doc = JsonDocument.ParseValue(ref reader);
        var el = doc.RootElement;

        var type = el.TryGetProperty("entity_type", out var t)
            ? t.GetString()
            : null;

        // Converter'sız options — sonsuz döngüyü önler
        var inner = new JsonSerializerOptions(options);
        for (int i = inner.Converters.Count - 1; i >= 0; i--)
            if (inner.Converters[i] is TelemetryConverter)
                inner.Converters.RemoveAt(i);

        return type switch
        {
            "compressor" => el.Deserialize<CompressorTelemetry>(inner)!,
            // §11.4 D3: segment gelince alan-yoklaması — gerçek boru mu, offtake/junction mu?
            "segment" => el.TryGetProperty("s_inlet_pressure_bar", out _)
                ? el.Deserialize<SegmentTelemetry>(inner)!   // gerçek boru parçası
                : el.Deserialize<PointTelemetry>(inner)!,    // offtake / junction noktası
            "ugs"        => el.Deserialize<StorageTelemetry>(inner)!,
            "border_entry" => el.Deserialize<BorderTelemetry>(inner)!,
            "fsru"       => el.Deserialize<FsruTelemetry>(inner)!,
            "lng_terminal" => el.Deserialize<LngTerminalTelemetry>(inner)!,
            "oil_pump"   => el.Deserialize<OilPumpTelemetry>(inner)!,
            "oil_storage" => el.Deserialize<OilStorageTelemetry>(inner)!,
            // Bilinmeyen tipte FIRLATMA: canli akis kirilmasin. Ham JSON zaten
            // MySQL'e kaydedilir; DB narrow acilimi sensor_definitions'a bagli
            // oldugu icin yeni tip tanimi eklenince otomatik devreye girer.
            _ => el.Deserialize<GenericTelemetry>(inner)!
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TelemetryBase value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
