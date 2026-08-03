using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCaDaClient.Models.Telemetry;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// BackgroundService — her PollIntervalSeconds'ta A endpoint'ini çeker,
/// sonucu SnapshotReceived olayıyla UI'a yayınlar.
/// Bir turun hatası döngüyü durdurmaz (§9).
/// </summary>
public sealed class LiveDataService : BackgroundService
{
    private readonly SCaDaApiClient _api;
    private readonly SCaDaApiOptions _opt;
    private readonly LiveSnapshotRepository? _repository;
    private readonly ILogger<LiveDataService> _log;

    // UI'ın abone olacağı en güncel anlık görüntü (düz harita — §11.2 D1)
    public event Action<IReadOnlyDictionary<string, TelemetryBase>>? SnapshotReceived;
    public IReadOnlyDictionary<string, TelemetryBase>? Latest { get; private set; }

    public LiveDataService(
        SCaDaApiClient api,
        IOptions<SCaDaApiOptions> opt,
        LiveSnapshotRepository? repository,
        ILogger<LiveDataService> log)
    {
        _api = api;
        _opt = opt.Value;
        _repository = repository;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(_opt.PollIntervalSeconds);
        using var timer = new PeriodicTimer(period);

        _log.LogInformation(
            "LiveDataService başlatıldı — poll aralığı: {Interval}s, BaseUrl: {BaseUrl}",
            _opt.PollIntervalSeconds, _opt.BaseUrl);

        do
        {
            try
            {
                var snapshot = await _api.GetLiveDataAsync(ct);
                Latest = snapshot.Entities;
                SnapshotReceived?.Invoke(snapshot.Entities);

                // DB yazımı — repository null ise atla (UI kullanımı)
                if (_repository is not null)
                {
                    var saveResult = await _repository.SaveAsync(snapshot, ct);
                    _log.LogInformation(
                        "[DB OK] snapshot_id={SnapshotId}, varlık={EntityCount}, " +
                        "narrow_telemetri={NarrowRows}, " +
                        "saklanan_snapshot={RetainedCount}, hedef={Server}:{Port}/{Database}",
                        saveResult.SnapshotId,
                        saveResult.EntityCount,
                        saveResult.NarrowTelemetryRows,
                        saveResult.RetainedSnapshotCount,
                        saveResult.DatabaseServer,
                        saveResult.DatabasePort,
                        saveResult.DatabaseName);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Gerçek kapanış (Ctrl+C / host shutdown) — döngüden çık.
                break;
            }
            catch (OperationCanceledException ex)
            {
                // ct iptal EDİLMEDİ: bu bir HTTP zaman aşımıdır (HttpClient.Timeout
                // TaskCanceledException fırlatır). Geçici hata — döngüyü DURDURMA,
                // sonraki turda tekrar dene. (Eskiden burada break vardı ve ilk
                // zaman aşımında istemci kalıcı olarak duruyordu.)
                _log.LogWarning(
                    "Canlı veri zaman aşımına uğradı ({Timeout}s), sonraki tur denenecek: {Msg}",
                    _opt.TimeoutSeconds, ex.Message);
            }
            catch (Exception ex)
            {
                // Bir tur hata olsa bile döngü durmaz — sonraki turda tekrar dener (§9)
                _log.LogWarning(
                    ex,
                    "Canlı veri çekilemedi veya MySQL'e kaydedilemedi ({ErrorCode}), " +
                    "sonraki tur denenecek",
                    GetErrorCode(ex));
            }
        }
        while (await timer.WaitForNextTickAsync(ct));

        _log.LogInformation("LiveDataService durduruldu");
    }

    private static string GetErrorCode(Exception ex)
    {
        var httpEx = ex;
        while (httpEx != null)
        {
            if (httpEx is System.Net.Http.HttpRequestException httpReq)
                return httpReq.Message;
            httpEx = httpEx.InnerException;
        }
        return ex.GetType().Name;
    }
}
