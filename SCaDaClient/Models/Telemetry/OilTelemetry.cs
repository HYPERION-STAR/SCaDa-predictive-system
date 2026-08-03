using System.Text.Json.Serialization;

namespace SCaDaClient.Models.Telemetry;

/// <summary>
/// Petrol pompa ünitesi (oil_pump) telemetrisi — PT-* / PS-* pompa istasyonları.
/// C-MAPSS "motoru"nun ham petrol tarafındaki karşılığı (6 sensör).
/// </summary>
public sealed class OilPumpTelemetry : TelemetryBase
{
    [JsonPropertyName("s_suction_pressure_bar")]
    public double SuctionPressureBar { get; set; }

    [JsonPropertyName("s_discharge_pressure_bar")]
    public double DischargePressureBar { get; set; }

    [JsonPropertyName("s_flow_m3_h")]
    public double FlowM3H { get; set; }

    [JsonPropertyName("s_pump_power_mw")]
    public double PumpPowerMw { get; set; }

    [JsonPropertyName("s_bearing_temp_c")]
    public double BearingTempC { get; set; }

    [JsonPropertyName("s_vibration_mm_s")]
    public double VibrationMmS { get; set; }
}

/// <summary>
/// Ham petrol depolama (oil_storage) telemetrisi — OILDEPO-* tankları (4 sensör).
/// </summary>
public sealed class OilStorageTelemetry : TelemetryBase
{
    [JsonPropertyName("s_tank_level_pct")]
    public double TankLevelPct { get; set; }

    [JsonPropertyName("s_inventory_m3")]
    public double InventoryM3 { get; set; }

    [JsonPropertyName("s_throughput_m3_h")]
    public double ThroughputM3H { get; set; }

    [JsonPropertyName("s_tank_temp_c")]
    public double TankTempC { get; set; }
}

/// <summary>
/// Bilinmeyen entity_type için dayanıklı geri düşüş (fallback).
/// Yalnızca taban alanları (entity_id, entity_type, quality, timestamp) taşır.
/// Amaç: sunucu ileride yeni bir tip eklediğinde canlı akışın KIRILMAMASI —
/// ham JSON yine de MySQL'e kaydedilir; DB narrow açılımı sensor_definitions
/// üzerinden çalışır, yani yeni tip için tanım eklenince otomatik devreye girer.
/// </summary>
public sealed class GenericTelemetry : TelemetryBase
{
}
