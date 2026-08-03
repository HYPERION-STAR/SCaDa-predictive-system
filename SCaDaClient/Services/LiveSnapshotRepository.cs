using System.Data;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SCaDaClient.Models;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

public sealed class LiveSnapshotRepository
{
    private readonly string _connectionString;
    private readonly MySqlConnectionStringBuilder _connectionOptions;

    public LiveSnapshotRepository(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
        _connectionOptions = new MySqlConnectionStringBuilder(_connectionString);
    }

    public async Task<LiveSnapshotSaveResult> SaveAsync(
        LiveDataSnapshot snapshot,
        CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand("sp_save_live_snapshot", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 15
        };
        command.Parameters.Add("p_captured_at", MySqlDbType.DateTime).Value = snapshot.CapturedAt;
        command.Parameters.Add("p_payload_json", MySqlDbType.JSON).Value = snapshot.RawJson;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                "sp_save_live_snapshot did not return a save result.");
        }

        return new LiveSnapshotSaveResult(
            reader.GetFieldValue<ulong>("snapshot_id"),
            reader.GetDateTime("captured_at"),
            reader.GetFieldValue<uint>("entity_count"),
            reader.GetInt64("retained_snapshot_count"),
            reader.GetInt32("narrow_telemetry_rows"),
            _connectionOptions.Server,
            _connectionOptions.Port,
            _connectionOptions.Database);
    }
}
