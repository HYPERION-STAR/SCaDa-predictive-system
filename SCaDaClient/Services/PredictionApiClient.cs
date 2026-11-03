using System.Net.Http.Json;
using System.Text.Json;
using SCaDaClient.Models;
using SCaDaClient.Options;
using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Services;

/// <summary>
/// Tahmin servisine HTTP istemcisi ÔÇö FastAPI /predict endpoint'ine cagri yapar.
/// ┬º5.1 ENTEGRASYON_TAHMIN_SISTEMI.md ile uyumlu.
/// </summary>
public sealed class PredictionApiClient : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    public PredictionApiClient(HttpClient http) => _http = http;

    /// <summary>
    /// Tek kompresor icin tahmin cagrisi.
    /// sensors + history (sliding window) gonderir, PredictionResult dondurur.
    /// </summary>
    public async Task<PredictionResult> PredictAsync(
        string id,
        CompressorTelemetry c,
        IEnumerable<TelemetryBase> history,
        CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PredictionApiClient));

        // Sens├Âr alanlarini Dictionary olarak hazirla
        var sensors = new Dictionary<string, double>
        {
            ["s_1_suction_pressure_bar"] = c.SuctionPressureBar,
            ["s_2_discharge_pressure_bar"] = c.DischargePressureBar,
            ["s_3_pressure_ratio"] = c.PressureRatio,
            ["s_4_suction_temp_c"] = c.SuctionTempC,
            ["s_5_discharge_temp_c"] = c.DischargeTempC,
            ["s_6_shaft_rpm"] = c.ShaftRpm,
            ["s_7_vibration_de_mm_s"] = c.VibrationDe,
            ["s_8_vibration_nde_mm_s"] = c.VibrationNde,
            ["s_9_axial_displacement_mm"] = c.AxialDisplacementMm,
            ["s_10_bearing_temp_1_c"] = c.BearingTemp1C,
            ["s_11_bearing_temp_2_c"] = c.BearingTemp2C,
            ["s_12_lube_oil_pressure_bar"] = c.LubeOilPressureBar,
            ["s_13_lube_oil_temp_c"] = c.LubeOilTempC,
            ["s_14_gas_flow_meter_m3_h"] = c.GasFlowMeter,
            ["s_15_seal_gas_pressure_bar"] = c.SealGasPressureBar,
            ["s_16_gas_flow_m3_h"] = c.GasFlow,
            ["s_17_power_mw"] = c.PowerMw,
            ["s_18_polytropic_efficiency"] = c.PolytropicEfficiency,
            ["s_19_surge_margin_pct"] = c.SurgeMarginPct,
            ["s_20_filter_dp_bar"] = c.FilterDpBar,
            ["s_21_torque_nm"] = c.TorqueNm,
            ["op_1_ambient_temp_c"] = c.AmbientTempC,
            ["op_2_inlet_pressure_bar"] = c.InletPressureBar,
            ["op_3_flow_demand_m3_h"] = c.FlowDemand,
            ["op_4_speed_setpoint_pct"] = c.SpeedSetpointPct,
        };

        // History'den onceki okumalari ayikla
        var historyList = new List<Dictionary<string, double>>();
        foreach (var h in history)
        {
            if (h is CompressorTelemetry ch)
            {
                historyList.Add(new Dictionary<string, double>
                {
                    ["s_1_suction_pressure_bar"] = ch.SuctionPressureBar,
                    ["s_2_discharge_pressure_bar"] = ch.DischargePressureBar,
                    ["s_3_pressure_ratio"] = ch.PressureRatio,
                    ["s_4_suction_temp_c"] = ch.SuctionTempC,
                    ["s_5_discharge_temp_c"] = ch.DischargeTempC,
                    ["s_6_shaft_rpm"] = ch.ShaftRpm,
                    ["s_7_vibration_de_mm_s"] = ch.VibrationDe,
                    ["s_8_vibration_nde_mm_s"] = ch.VibrationNde,
                    ["s_9_axial_displacement_mm"] = ch.AxialDisplacementMm,
                    ["s_10_bearing_temp_1_c"] = ch.BearingTemp1C,
                    ["s_11_bearing_temp_2_c"] = ch.BearingTemp2C,
                    ["s_12_lube_oil_pressure_bar"] = ch.LubeOilPressureBar,
                    ["s_13_lube_oil_temp_c"] = ch.LubeOilTempC,
                    ["s_14_gas_flow_meter_m3_h"] = ch.GasFlowMeter,
                    ["s_15_seal_gas_pressure_bar"] = ch.SealGasPressureBar,
                    ["s_16_gas_flow_m3_h"] = ch.GasFlow,
                    ["s_17_power_mw"] = ch.PowerMw,
                    ["s_18_polytropic_efficiency"] = ch.PolytropicEfficiency,
                    ["s_19_surge_margin_pct"] = ch.SurgeMarginPct,
                    ["s_20_filter_dp_bar"] = ch.FilterDpBar,
                    ["s_21_torque_nm"] = ch.TorqueNm,
                    ["op_1_ambient_temp_c"] = ch.AmbientTempC,
                    ["op_2_inlet_pressure_bar"] = ch.InletPressureBar,
                    ["op_3_flow_demand_m3_h"] = ch.FlowDemand,
                    ["op_4_speed_setpoint_pct"] = ch.SpeedSetpointPct,
                });
            }
        }

        var payload = new
        {
            entity_id = id,
            entity_type = "compressor",
            sensors = sensors,
            history = historyList
        };

        var resp = await _http.PostAsJsonAsync("/predict", payload, ct);
        resp.EnsureSuccessStatusCode();

        return (await resp.Content.ReadFromJsonAsync<PredictionResult>(ct))!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
