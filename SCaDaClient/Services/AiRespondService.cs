using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCaDaClient.Models;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// BackgroundService — model servisinin /hub_state ucunu periyodik çeker ve
/// ai_respond log tablosuna yazar. LiveDataService ile aynı hata politikası:
/// bir turun hatası döngüyü durdurmaz.
/// </summary>
public sealed class AiRespondService : BackgroundService
{
    private readonly AiHubClient _client;
    private readonly AiRespondRepository _repository;
    private readonly AiHubOptions _opt;
    private readonly ILogger<AiRespondService> _log;

    public event Action<AiHubState>? HubStateReceived;
    public AiHubState? Latest { get; private set; }

    public AiRespondService(
        AiHubClient client,
        AiRespondRepository repository,
        IOptions<AiHubOptions> opt,
        ILogger<AiRespondService> log)
    {
        _client = client;
        _repository = repository;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(_opt.PollIntervalSeconds);
        using var timer = new PeriodicTimer(period);

        _log.LogInformation(
            "AiRespondService başlatıldı — poll aralığı: {Interval}s, BaseUrl: {BaseUrl}",
            _opt.PollIntervalSeconds, _opt.BaseUrl);

        do
        {
            try
            {
                var fetch = await _client.GetHubStateAsync(ct);
                Latest = fetch.State;
                HubStateReceived?.Invoke(fetch.State);

                var result = await _repository.SaveAsync(fetch, ct);

                if (result.Skipped)
                {
                    _log.LogDebug(
                        "[AI ATLANDI] gövde değişmemiş — düğüm={Nodes}, segment={Segments}",
                        result.NodeCount, result.SegmentCount);
                }
                else
                {
                    _log.LogInformation(
                        "[AI OK] respond_id={RespondId}, düğüm={Nodes}, segment={Segments}, " +
                        "kritik={Critical}, sızıntı={Leak}, gecikme={Latency}ms",
                        result.RespondId, result.NodeCount, result.SegmentCount,
                        result.CriticalNodeCount, result.LeakSegmentCount, fetch.LatencyMs);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                // HTTP zaman aşımı — geçici, döngüyü durdurma.
                _log.LogWarning(
                    "/hub_state zaman aşımına uğradı ({Timeout}s), sonraki tur denenecek: {Msg}",
                    _opt.TimeoutSeconds, ex.Message);
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex, "/hub_state çekilemedi veya ai_respond'a yazılamadı, sonraki tur denenecek");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));

        _log.LogInformation("AiRespondService durduruldu");
    }
}
