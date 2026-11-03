using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// BackgroundService — ai_respond'daki bekleyen satırları karşı bilgisayara
/// HTTP POST ile JSON olarak iletir (outbox deseni).
///
/// Neden outbox: gönderim DB yazımından ayrık. Karşı bilgisayar kapalıyken
/// satırlar 'pending' olarak birikir, ağ dönünce sırayla ve tam olarak gider —
/// canlı veri toplama hiç etkilenmez.
/// </summary>
public sealed class AiForwardService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly HttpClient _http;
    private readonly AiRespondRepository _repository;
    private readonly AiForwardOptions _opt;
    private readonly ILogger<AiForwardService> _log;

    // respond_id → şimdiye kadarki deneme sayısı
    private readonly Dictionary<ulong, int> _attempts = new();

    public AiForwardService(
        HttpClient http,
        AiRespondRepository repository,
        IOptions<AiForwardOptions> opt,
        ILogger<AiForwardService> log)
    {
        _http = http;
        _repository = repository;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.TargetUrl))
        {
            _log.LogInformation(
                "AiForwardService kapalı — AiForward__TargetUrl ayarlanmamış. " +
                "Satırlar ai_respond'da send_status='pending' olarak birikecek.");
            return;
        }

        var period = TimeSpan.FromSeconds(_opt.FlushIntervalSeconds);
        using var timer = new PeriodicTimer(period);

        _log.LogInformation(
            "AiForwardService başlatıldı — hedef: {Target}, tur: {Interval}s, batch: {Batch}",
            _opt.TargetUrl, _opt.FlushIntervalSeconds, _opt.BatchSize);

        do
        {
            try
            {
                var pending = await _repository.FetchPendingAsync(_opt.BatchSize, ct);
                if (pending.Count == 0) continue;

                int sent = 0;
                foreach (var envelope in pending)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        // StringContent → Content-Length'li gövde. JsonContent.Create
                        // chunked transfer-encoding kullanır ve basit HTTP sunucuları
                        // (python http.server gibi) gövdeyi boş okur.
                        var json = JsonSerializer.Serialize(envelope, JsonOptions);
                        using var request = new HttpRequestMessage(HttpMethod.Post, _opt.TargetUrl)
                        {
                            Content = new StringContent(json, Encoding.UTF8, "application/json")
                        };
                        if (!string.IsNullOrWhiteSpace(_opt.ApiKey))
                            request.Headers.Add("X-Api-Key", _opt.ApiKey);

                        using var response = await _http.SendAsync(request, ct);
                        response.EnsureSuccessStatusCode();

                        await _repository.MarkSentAsync(envelope.RespondId, ct);
                        _attempts.Remove(envelope.RespondId);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException
                                               || !ct.IsCancellationRequested)
                    {
                        var attempt = _attempts.GetValueOrDefault(envelope.RespondId) + 1;
                        _attempts[envelope.RespondId] = attempt;
                        var giveUp = attempt >= _opt.MaxAttempts;

                        await _repository.MarkFailedAsync(
                            envelope.RespondId, ex.Message, giveUp, CancellationToken.None);

                        if (giveUp)
                        {
                            _attempts.Remove(envelope.RespondId);
                            _log.LogError(
                                "[GÖNDERİM BAŞARISIZ] respond_id={Id} {Attempts} denemede " +
                                "gönderilemedi, 'failed' işaretlendi: {Msg}",
                                envelope.RespondId, attempt, ex.Message);
                        }
                        else
                        {
                            _log.LogWarning(
                                "[GÖNDERİM HATA] respond_id={Id}, deneme {Attempt}/{Max}: {Msg}",
                                envelope.RespondId, attempt, _opt.MaxAttempts, ex.Message);
                        }

                        // Sıra bozulmasın: bu turda kalan satırları bırak, sonraki turda dene.
                        break;
                    }
                }

                if (sent > 0)
                {
                    _log.LogInformation(
                        "[GÖNDERİLDİ] {Sent} satır → {Target}", sent, _opt.TargetUrl);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Gönderim turu başarısız, sonraki turda denenecek");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));

        _log.LogInformation("AiForwardService durduruldu");
    }
}
