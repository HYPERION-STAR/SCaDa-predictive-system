using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SCaDaClient.Models;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// PredictionDbWriter — PredictionReady olayına abone olur, her sonucu MySQL
/// `predictions` tablosuna yazar (Kişi 4 şeması). Batch + periyodik flush.
/// </summary>
public sealed class PredictionDbWriter : IDisposable
{
    private readonly ILogger<PredictionDbWriter> _log;
    private readonly string _connectionString;
    private bool _disposed;

    private const string ModelVersion = "SCaDa-rf-214";
    private const int FlushIntervalMs = 5000;
    private const int BatchSize = 100;

    private readonly List<PredictionRecord> _buffer = new();
    private readonly object _bufferLock = new();
    private Timer? _flushTimer;

    private sealed class PredictionRecord
    {
        public string EntityId { get; set; } = "";
        public DateTime Ts { get; set; }
        public double? Rul { get; set; }
        public double? HealthScore { get; set; }
        public bool? Anomaly { get; set; }
        public string? FaultMode { get; set; }
        public string Source { get; set; } = "";
        public string Status { get; set; } = "running";
    }

    public PredictionDbWriter(
        ILogger<PredictionDbWriter> log,
        IOptions<DatabaseOptions> dbOpt)
    {
        _log = log;
        _connectionString = dbOpt.Value.ConnectionString;

        _flushTimer = new Timer(
            callback: FlushBuffer,
            state: null,
            dueTime: FlushIntervalMs,
            period: FlushIntervalMs);

        _log.LogInformation(
            "PredictionDbWriter baslatildi — batch: {BatchSize}, flush: {FlushInterval}ms",
            BatchSize, FlushIntervalMs);
    }

    public void Subscribe(PredictionService service)
    {
        service.PredictionReady += OnPredictionReady;
        _log.LogInformation("PredictionDbWriter PredictionReady'e baglandi");
    }

    private void OnPredictionReady(PredictionResult result)
    {
        if (_disposed) return;

        var record = new PredictionRecord
        {
            EntityId = result.EntityId,
            Ts = DateTime.UtcNow,
            Rul = result.Rul,
            HealthScore = result.HealthScore,
            Anomaly = result.Anomaly,
            FaultMode = result.FaultMode,
            Source = result.Source,
            Status = result.Status
        };

        lock (_bufferLock)
        {
            _buffer.Add(record);
            if (_buffer.Count >= BatchSize)
                FlushBuffer(null);
        }
    }

    // health_score (0..1) -> Kişi 1 model sözlüğü (healthy/degrading/critical)
    private static string HealthState(double? score)
    {
        if (!score.HasValue) return "unknown";
        if (score.Value > 0.7) return "healthy";
        if (score.Value > 0.3) return "degrading";
        return "critical";
    }

    private void FlushBuffer(object? state)
    {
        List<PredictionRecord> batch;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            batch = new List<PredictionRecord>(_buffer);
            _buffer.Clear();
        }

        try
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            using var cmd = new MySqlCommand(
                @"INSERT INTO predictions
                    (recorded_at, entity_id, rul_value, health_state,
                     fault_mode, anomaly_score, model_version, created_at)
                  VALUES
                    (@recorded_at, @entity_id, @rul_value, @health_state,
                     @fault_mode, @anomaly_score, @model_version, @created_at)",
                conn);

            int inserted = 0;
            foreach (var r in batch)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@recorded_at", r.Ts);
                cmd.Parameters.AddWithValue("@entity_id", r.EntityId ?? "");
                cmd.Parameters.AddWithValue("@rul_value",
                    r.Rul.HasValue ? (object)r.Rul.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@health_state", HealthState(r.HealthScore));
                cmd.Parameters.AddWithValue("@fault_mode",
                    !string.IsNullOrEmpty(r.FaultMode) ? (object)r.FaultMode : DBNull.Value);
                cmd.Parameters.AddWithValue("@anomaly_score",
                    r.Anomaly.HasValue ? (object)(r.Anomaly.Value ? 1.0 : 0.0) : DBNull.Value);
                cmd.Parameters.AddWithValue("@model_version", ModelVersion);
                cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow);

                cmd.ExecuteNonQuery();
                inserted++;
            }

            _log.LogDebug("DB'ye {Inserted} tahmin yazildi (predictions)", inserted);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DB yazim hatasi: {Count} tahmin kaydedilemedi", batch.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FlushBuffer(null);
        _flushTimer?.Dispose();
    }
}
