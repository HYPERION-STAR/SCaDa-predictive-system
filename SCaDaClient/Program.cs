using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using SCaDaClient.Models.Telemetry;
using SCaDaClient.Options;
using SCaDaClient.Parsing;
using SCaDaClient.Services;

namespace SCaDaClient;

/// <summary>
/// SCaDa Canlı Veri Okuyucu — ana uygulama giriş noktası.
/// BackgroundService ile sürekli poll yapar, SnapshotReceived olayıyla UI'a yayınlar.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // appsettings.json'i uygulama ikilisinin yanindan oku. Aksi halde
                // `dotnet run` calisma dizini (repo koku) baz alinir ve dosya
                // bulunamayip sinif varsayilanlarina dusulur (orn. TimeoutSeconds=15).
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                // Secrets supplied by the host must override JSON placeholders.
                config.AddEnvironmentVariables();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                // 1. Yapılandırma seçenekleri
                services.Configure<SCaDaApiOptions>(
                    context.Configuration.GetSection(SCaDaApiOptions.SectionName));
                services.AddOptions<DatabaseOptions>()
                    .Bind(context.Configuration.GetSection(DatabaseOptions.SectionName))
                    .Validate(
                        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                        "Database connection string is required. Set Database__ConnectionString.")
                    .ValidateOnStart();

                // 2. JSON parser (singleton — options'ları tek yerde tutar)
                services.AddSingleton<SCaDaJsonParser>();

                // 3. HttpClient + SCaDaApiClient (Polly retry ile)
                services.AddHttpClient<SCaDaApiClient>((sp, client) =>
                {
                    var opt = sp.GetRequiredService<IOptions<SCaDaApiOptions>>().Value;
                    client.BaseAddress = new Uri(opt.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
                    client.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                })
                .AddTransientHttpErrorPolicy(policy =>
                    policy.WaitAndRetryAsync(3, attempt =>
                        TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)))); // 0.5s, 1s, 2s

                // 4. NodeDetailClient (tek düğüm çağrıları için)
                services.AddSingleton<NodeDetailClient>();

                // 5 saniyelik snapshot'ları MySQL'e transaction ile yazar.
                services.AddSingleton<LiveSnapshotRepository>();

                // 5. LiveDataService — BackgroundService olarak çalışır
                services.AddSingleton<LiveDataService>();
                services.AddHostedService(sp => sp.GetRequiredService<LiveDataService>());

                // 6. Tahmin zinciri: model servisi (/predict) → predictions tablosu
                services.Configure<PredictionApiOptions>(
                    context.Configuration.GetSection(PredictionApiOptions.SectionName));
                services.AddHttpClient<PredictionApiClient>((sp, client) =>
                {
                    var opt = sp.GetRequiredService<IOptions<PredictionApiOptions>>().Value;
                    client.BaseAddress = new Uri(opt.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
                });
                services.AddSingleton<PredictionService>();   // SnapshotReceived → /predict
                services.AddSingleton<PredictionDbWriter>();   // PredictionReady → MySQL predictions
            })
            .Build();

        // --- UI bağlama noktası (örnek) ---
        // Gerçek uygulamada burada harita/detay paneli UI thread'i bağlanır:
        //
        // var liveData = host.Services.GetRequiredService<LiveDataService>();
        // liveData.SnapshotReceived += live =>
        // {
        //     // Harita: her turda düğüm renklerini güncelle (düz harita — §11.2 D1)
        //     foreach (var (entityId, tel) in live)
        //         if (tel is CompressorTelemetry c)
        //             MapView.SetNodeColor(c.EntityId, HealthColor(c)); // §8 kural
        // };
        //
        // Detay paneli: tıklamada tek düğüm çek
        // var nodeClient = host.Services.GetRequiredService<NodeDetailClient>();
        // async void OnNodeClicked(string nodeId)
        // {
        //     var detail = await nodeClient.LoadAsync(nodeId);
        //     DetailPanel.Bind(detail);
        // }

        // Konsol demo: SnapshotReceived'ı konsola yazdır (düz harita — §11.2 D1)
        var demoLiveData = host.Services.GetRequiredService<LiveDataService>();
        demoLiveData.SnapshotReceived += live =>
        {
            int comp = live.Values.Count(t => t is CompressorTelemetry);
            int seg  = live.Values.Count(t => t is SegmentTelemetry);
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss}] Canlı: " +
                $"{live.Count} varlık ({comp} kompresör, {seg} segment)");
        };

        // Tahmin zincirini bağla: snapshot → PredictionService (/predict) → predictions tablosu.
        // PredictionService ctor'u LiveDataService.SnapshotReceived'a abone olur;
        // PredictionDbWriter de PredictionReady'e — böylece model çıktısı MySQL'e akar.
        var predictionService = host.Services.GetRequiredService<PredictionService>();
        var predictionDbWriter = host.Services.GetRequiredService<PredictionDbWriter>();
        predictionDbWriter.Subscribe(predictionService);

        Console.WriteLine("SCaDa Canlı Veri Okuyucu başlatılıyor... (durdurmak için Ctrl+C)");
        Console.WriteLine();

        await host.RunAsync();
    }
}
