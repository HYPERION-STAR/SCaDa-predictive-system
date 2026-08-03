using System.Text.Json.Serialization;

namespace SCaDaClient.Models.Telemetry;

/// <summary>
/// Segment (boru hattı parçası) telemetrisi.
/// </summary>
public sealed class SegmentTelemetry : TelemetryBase
{
    [JsonPropertyName("s_inlet_pressure_bar")]
    public double InletPressureBar { get; set; }

    [JsonPropertyName("s_outlet_pressure_bar")]
    public double OutletPressureBar { get; set; }

    [JsonPropertyName("s_dp_bar")]
    public double DpBar { get; set; }

    [JsonPropertyName("s_flow_m3_h")]
    public double FlowM3H { get; set; }

    [JsonPropertyName("s_outflow_m3_h")]
    public double OutflowM3H { get; set; }

    [JsonPropertyName("s_mass_imbalance_pct")]
    public double MassImbalancePct { get; set; }

    [JsonPropertyName("s_temperature_c")]
    public double TemperatureC { get; set; }

    [JsonPropertyName("s_cathodic_protection_v")]
    public double CathodicProtectionV { get; set; }

    // Bilinmeyen/ek alanlar TelemetryBase.Extra (tek [JsonExtensionData]) ile yutulur.
    // Burada AYRICA tanımlanırsa iki extension-data üyesi olur -> deserialize hatası.
}

/// <summary>
/// UGS (Yer Altı Gaz Depolama) telemetrisi.
/// </summary>
public sealed class StorageTelemetry : TelemetryBase
{
    [JsonPropertyName("s_storage_level_pct")]
    public double StorageLevelPct { get; set; }

    [JsonPropertyName("s_inventory_mcm")]
    public double InventoryMcm { get; set; }

    [JsonPropertyName("s_injection_flow_m3_h")]
    public double InjectionFlowM3H { get; set; }

    [JsonPropertyName("s_withdrawal_flow_m3_h")]
    public double WithdrawalFlowM3H { get; set; }

    [JsonPropertyName("s_pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("s_cavern_temp_c")]
    public double CavernTempC { get; set; }

    [JsonPropertyName("s_daily_capacity_mcm_day")]
    public double DailyCapacityMcmDay { get; set; }

    [JsonPropertyName("s_working_capacity_pct")]
    public double WorkingCapacityPct { get; set; }
}

/// <summary>
/// Sınır giriş (border_entry) telemetrisi.
/// </summary>
public sealed class BorderTelemetry : TelemetryBase
{
    [JsonPropertyName("flow_m3_h")]
    public double FlowM3H { get; set; }

    [JsonPropertyName("pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("temperature_c")]
    public double TemperatureC { get; set; }

    [JsonPropertyName("calorific_value_mj_m3")]
    public double CalorificValueMjM3 { get; set; }
}

/// <summary>
/// FSRU (Sıvılaştırılmış Doğal Gaz Tekne) telemetrisi.
/// </summary>
public sealed class FsruTelemetry : TelemetryBase
{
    [JsonPropertyName("s_storage_level_pct")]
    public double StorageLevelPct { get; set; }

    [JsonPropertyName("s_pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("s_boil_off_rate_pct")]
    public double BoilOffRatePct { get; set; }

    [JsonPropertyName("s_liquid_flow_m3_h")]
    public double LiquidFlowM3H { get; set; }

    [JsonPropertyName("s_vapor_flow_m3_h")]
    public double VaporFlowM3H { get; set; }

    [JsonPropertyName("s_tank_temp_c")]
    public double TankTempC { get; set; }
}

/// <summary>
/// LNG Terminali telemetrisi.
/// </summary>
public sealed class LngTerminalTelemetry : TelemetryBase
{
    [JsonPropertyName("s_storage_level_pct")]
    public double StorageLevelPct { get; set; }

    [JsonPropertyName("s_pressure_bar")]
    public double PressureBar { get; set; }

    [JsonPropertyName("s_boil_off_rate_pct")]
    public double BoilOffRatePct { get; set; }

    [JsonPropertyName("s_regas_flow_m3_h")]
    public double RegasFlowM3H { get; set; }

    [JsonPropertyName("s_tank_temp_c")]
    public double TankTempC { get; set; }
}
