using System.Text.Json.Serialization;

namespace SCaDaClient.Models.Telemetry;

/// <summary>
/// Kompresör telemetrisi — 21 sensör + 4 op + status.
/// Alan adları SCaDa_live_snapshot.json ile birebir eşleşir.
/// </summary>
public sealed class CompressorTelemetry : TelemetryBase
{
    // Sensör alanları (s_*)
    [JsonPropertyName("s_1_suction_pressure_bar")]
    public double SuctionPressureBar { get; set; }

    [JsonPropertyName("s_2_discharge_pressure_bar")]
    public double DischargePressureBar { get; set; }

    [JsonPropertyName("s_3_pressure_ratio")]
    public double PressureRatio { get; set; }

    [JsonPropertyName("s_4_suction_temp_c")]
    public double SuctionTempC { get; set; }

    [JsonPropertyName("s_5_discharge_temp_c")]
    public double DischargeTempC { get; set; }

    [JsonPropertyName("s_6_shaft_rpm")]
    public double ShaftRpm { get; set; }

    [JsonPropertyName("s_7_vibration_de_mm_s")]
    public double VibrationDe { get; set; }

    [JsonPropertyName("s_8_vibration_nde_mm_s")]
    public double VibrationNde { get; set; }

    [JsonPropertyName("s_9_axial_displacement_mm")]
    public double AxialDisplacementMm { get; set; }

    [JsonPropertyName("s_10_bearing_temp_1_c")]
    public double BearingTemp1C { get; set; }

    [JsonPropertyName("s_11_bearing_temp_2_c")]
    public double BearingTemp2C { get; set; }

    [JsonPropertyName("s_12_lube_oil_pressure_bar")]
    public double LubeOilPressureBar { get; set; }

    [JsonPropertyName("s_13_lube_oil_temp_c")]
    public double LubeOilTempC { get; set; }

    [JsonPropertyName("s_14_gas_flow_meter_m3_h")]
    public double GasFlowMeter { get; set; }

    [JsonPropertyName("s_15_seal_gas_pressure_bar")]
    public double SealGasPressureBar { get; set; }

    [JsonPropertyName("s_16_gas_flow_m3_h")]
    public double GasFlow { get; set; }

    [JsonPropertyName("s_17_power_mw")]
    public double PowerMw { get; set; }

    [JsonPropertyName("s_18_polytropic_efficiency")]
    public double PolytropicEfficiency { get; set; }

    [JsonPropertyName("s_19_surge_margin_pct")]
    public double SurgeMarginPct { get; set; }

    [JsonPropertyName("s_20_filter_dp_bar")]
    public double FilterDpBar { get; set; }

    [JsonPropertyName("s_21_torque_nm")]
    public double TorqueNm { get; set; }

    // Operatör alanları (op_*)
    [JsonPropertyName("op_1_ambient_temp_c")]
    public double AmbientTempC { get; set; }

    [JsonPropertyName("op_2_inlet_pressure_bar")]
    public double InletPressureBar { get; set; }

    [JsonPropertyName("op_3_flow_demand_m3_h")]
    public double FlowDemand { get; set; }

    [JsonPropertyName("op_4_speed_setpoint_pct")]
    public double SpeedSetpointPct { get; set; }

    // Durum bilgisi
    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // running / standby
}
