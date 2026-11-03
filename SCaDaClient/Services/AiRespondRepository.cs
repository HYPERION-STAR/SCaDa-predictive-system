using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SCaDaClient.Models;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// /hub_state yanıtlarını ai_respond log tablosuna yazar.
/// Her poll turu tek satır; gövde payload_json'da olduğu gibi saklanır.
/// </summary>
public sealed class AiRespondRepository
{
    private readonly string _connectionString;
    private readonly AiHubOptions _hubOptions;

    public AiRespondRepository(
        IOptions<DatabaseOptions> options,
        IOptions<AiHubOptions> hubOptions)
    {
        _connectionString = options.Value.ConnectionString;
        _hubOptions = hubOptions.Value;
    }

    public async Task<AiRespondSaveResult> SaveAsync(AiHubFetch fetch, CancellationToken ct)
    {
        var state = fetch.State;
        var sha256 = Sha256Hex(fetch.RawJson);

        var criticalNodes = state.Nodes
            .Count(node => node.Health < _hubOptions.CriticalHealthThreshold);
        var leakSegments = state.Segments.Count(segment => segment.Leak);
        double? minHealth = state.Nodes.Count > 0 ? state.Nodes.Min(n => n.Health) : null;
        double? minRul = state.Nodes.Count > 0 ? state.Nodes.Min(n => n.Rul) : null;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Gövde bir öncekiyle birebir aynıysa log'u şişirme (model yavaş güncelleniyorsa).
        if (_hubOptions.SkipUnchanged)
        {
            await using var dedupe = new MySqlCommand(
                """
                SELECT payload_sha256 FROM ai_respond
                ORDER BY respond_id DESC LIMIT 1
                """, connection);
            var lastSha = await dedupe.ExecuteScalarAsync(ct) as string;
            if (string.Equals(lastSha, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new AiRespondSaveResult(
                    0, state.Nodes.Count, state.Segments.Count,
                    criticalNodes, leakSegments, Skipped: true);
            }
        }

        await using var command = new MySqlCommand(
            """
            INSERT INTO ai_respond
                (fetched_at, source_url, http_status, latency_ms, model_updated_at,
                 node_count, segment_count, critical_node_count, leak_segment_count,
                 min_health, min_rul, payload_bytes, payload_sha256, payload_json)
            VALUES
                (@fetched_at, @source_url, @http_status, @latency_ms, @model_updated_at,
                 @node_count, @segment_count, @critical_node_count, @leak_segment_count,
                 @min_health, @min_rul, @payload_bytes, @payload_sha256, @payload_json)
            """, connection)
        {
            CommandTimeout = 15
        };

        command.Parameters.Add("@fetched_at", MySqlDbType.DateTime).Value = fetch.FetchedAt;
        command.Parameters.Add("@source_url", MySqlDbType.VarChar).Value = fetch.SourceUrl;
        command.Parameters.Add("@http_status", MySqlDbType.Int16).Value = fetch.HttpStatus;
        command.Parameters.Add("@latency_ms", MySqlDbType.Int32).Value = fetch.LatencyMs;
        command.Parameters.Add("@model_updated_at", MySqlDbType.DateTime).Value =
            (object?)state.UpdatedAt ?? DBNull.Value;
        command.Parameters.Add("@node_count", MySqlDbType.Int16).Value = state.Nodes.Count;
        command.Parameters.Add("@segment_count", MySqlDbType.Int16).Value = state.Segments.Count;
        command.Parameters.Add("@critical_node_count", MySqlDbType.Int16).Value = criticalNodes;
        command.Parameters.Add("@leak_segment_count", MySqlDbType.Int16).Value = leakSegments;
        command.Parameters.Add("@min_health", MySqlDbType.Decimal).Value =
            (object?)minHealth ?? DBNull.Value;
        command.Parameters.Add("@min_rul", MySqlDbType.Decimal).Value =
            (object?)minRul ?? DBNull.Value;
        command.Parameters.Add("@payload_bytes", MySqlDbType.Int32).Value =
            Encoding.UTF8.GetByteCount(fetch.RawJson);
        command.Parameters.Add("@payload_sha256", MySqlDbType.String).Value = sha256;
        command.Parameters.Add("@payload_json", MySqlDbType.JSON).Value = fetch.RawJson;

        await command.ExecuteNonQueryAsync(ct);

        return new AiRespondSaveResult(
            (ulong)command.LastInsertedId,
            state.Nodes.Count,
            state.Segments.Count,
            criticalNodes,
            leakSegments,
            Skipped: false);
    }

    /// <summary>
    /// Henüz gönderilmemiş satırları en eskiden başlayarak okur (outbox kuyruğu).
    /// </summary>
    public async Task<IReadOnlyList<AiRespondEnvelope>> FetchPendingAsync(
        int batchSize, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            $"""
            {EnvelopeColumns}
            WHERE send_status = 'pending'
            ORDER BY respond_id
            LIMIT @batch
            """, connection);
        command.Parameters.Add("@batch", MySqlDbType.Int32).Value = batchSize;

        return await ReadEnvelopesAsync(command, ct);
    }

    /// <summary>
    /// Verilen id'den SONRAKİ satırları sırayla okur — karşı bilgisayarın
    /// artımlı çekmesi (cursor) için. sinceId=0 ise baştan başlar.
    /// </summary>
    public async Task<IReadOnlyList<AiRespondEnvelope>> FetchSinceAsync(
        ulong sinceId, int limit, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            $"""
            {EnvelopeColumns}
            WHERE respond_id > @since
            ORDER BY respond_id
            LIMIT @limit
            """, connection);
        command.Parameters.Add("@since", MySqlDbType.UInt64).Value = sinceId;
        command.Parameters.Add("@limit", MySqlDbType.Int32).Value = limit;

        return await ReadEnvelopesAsync(command, ct);
    }

    /// <summary>En son yazılan satır (yoksa null).</summary>
    public async Task<AiRespondEnvelope?> FetchLatestAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            $"""
            {EnvelopeColumns}
            ORDER BY respond_id DESC
            LIMIT 1
            """, connection);

        var rows = await ReadEnvelopesAsync(command, ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>Sağlık ucu için: en büyük respond_id ve toplam satır sayısı.</summary>
    public async Task<(ulong LatestId, long RowCount)> GetStatusAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            "SELECT COALESCE(MAX(respond_id), 0) AS latest_id, COUNT(*) AS row_count FROM ai_respond",
            connection);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, 0);

        return (reader.GetFieldValue<ulong>(reader.GetOrdinal("latest_id")),
                reader.GetInt64("row_count"));
    }

    private const string EnvelopeColumns =
        """
        SELECT respond_id, fetched_at, source_url, model_updated_at,
               node_count, segment_count, critical_node_count, leak_segment_count,
               min_health, min_rul, payload_json
        FROM ai_respond
        """;

    private static async Task<List<AiRespondEnvelope>> ReadEnvelopesAsync(
        MySqlCommand command, CancellationToken ct)
    {
        var results = new List<AiRespondEnvelope>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            using var payload = JsonDocument.Parse(reader.GetString("payload_json"));
            results.Add(new AiRespondEnvelope(
                reader.GetFieldValue<ulong>(reader.GetOrdinal("respond_id")),
                reader.GetDateTime("fetched_at"),
                reader.GetString("source_url"),
                reader.IsDBNull(reader.GetOrdinal("model_updated_at"))
                    ? null : reader.GetDateTime("model_updated_at"),
                reader.GetInt32("node_count"),
                reader.GetInt32("segment_count"),
                reader.GetInt32("critical_node_count"),
                reader.GetInt32("leak_segment_count"),
                reader.IsDBNull(reader.GetOrdinal("min_health"))
                    ? null : reader.GetDecimal("min_health"),
                reader.IsDBNull(reader.GetOrdinal("min_rul"))
                    ? null : reader.GetDecimal("min_rul"),
                payload.RootElement.Clone()));
        }

        return results;
    }

    public async Task MarkSentAsync(ulong respondId, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            """
            UPDATE ai_respond
            SET send_status = 'sent', sent_at = CURRENT_TIMESTAMP(6), send_error = NULL
            WHERE respond_id = @id
            """, connection);
        command.Parameters.Add("@id", MySqlDbType.UInt64).Value = respondId;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gönderim hatasını kaydeder. Deneme sayısı sınırı aşıldıysa satır 'failed'
    /// olur ve kuyruğu tıkamaz; aksi halde 'pending' kalıp tekrar denenir.
    /// </summary>
    public async Task MarkFailedAsync(
        ulong respondId, string error, bool giveUp, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(
            """
            UPDATE ai_respond
            SET send_status = @status, send_error = @error
            WHERE respond_id = @id
            """, connection);
        command.Parameters.Add("@status", MySqlDbType.VarChar).Value =
            giveUp ? "failed" : "pending";
        command.Parameters.Add("@error", MySqlDbType.VarChar).Value =
            error.Length > 500 ? error[..500] : error;
        command.Parameters.Add("@id", MySqlDbType.UInt64).Value = respondId;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
