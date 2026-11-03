using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCaDaClient.Options;

namespace SCaDaClient.Services;

/// <summary>
/// Karşı bilgisayarın ai_respond verisini ÇEKMESİ için açılan HTTP uçları.
///
/// GET /health                       → yayın ayakta mı + en son respond_id
/// GET /ai_respond/latest            → en son satır (tek nesne)
/// GET /ai_respond?since=N&amp;limit=M   → N'den sonraki satırlar (artımlı çekim)
/// </summary>
public static class AiServeEndpoints
{
    public static void Map(IApplicationBuilder app)
    {
        var opt = app.ApplicationServices.GetRequiredService<IOptions<AiServeOptions>>().Value;

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            // Kök: tarayıcıdan açan biri neyin çalıştığını ve hangi uçların
            // olduğunu görsün. Aksi halde "/" 404 döner ve yayın kapalı sanılır.
            endpoints.MapGet("/", (HttpContext ctx) =>
            {
                if (!Authorized(ctx, opt)) return Results.Unauthorized();

                return Results.Ok(new
                {
                    service = "SCaDaClient/ai_respond",
                    status = "yayın çalışıyor",
                    endpoints = new[]
                    {
                        "GET /health",
                        "GET /ai_respond/latest",
                        "GET /ai_respond?since=N&limit=M"
                    },
                    api_key_required = !string.IsNullOrWhiteSpace(opt.ApiKey)
                });
            });

            endpoints.MapGet("/health", async (HttpContext ctx, AiRespondRepository repo) =>
            {
                if (!Authorized(ctx, opt)) return Results.Unauthorized();

                var (latestId, rowCount) = await repo.GetStatusAsync(ctx.RequestAborted);
                return Results.Ok(new
                {
                    ok = true,
                    service = "SCaDaClient/ai_respond",
                    latest_respond_id = latestId,
                    row_count = rowCount,
                    server_time = DateTime.Now
                });
            });

            endpoints.MapGet("/ai_respond/latest", async (HttpContext ctx, AiRespondRepository repo) =>
            {
                if (!Authorized(ctx, opt)) return Results.Unauthorized();

                var latest = await repo.FetchLatestAsync(ctx.RequestAborted);
                return latest is null
                    ? Results.NotFound(new { error = "ai_respond tablosu boş." })
                    : Results.Ok(latest);
            });

            endpoints.MapGet("/ai_respond", async (
                HttpContext ctx, AiRespondRepository repo, ulong? since, int? limit) =>
            {
                if (!Authorized(ctx, opt)) return Results.Unauthorized();

                var take = Math.Clamp(limit ?? 100, 1, opt.MaxLimit);
                var rows = await repo.FetchSinceAsync(since ?? 0, take, ctx.RequestAborted);

                return Results.Ok(new
                {
                    count = rows.Count,
                    // Karşı taraf bir sonraki isteğinde bunu ?since= olarak
                    // göndererek aynı satırı iki kez almadan devam eder.
                    next_since = rows.Count > 0 ? rows[^1].RespondId : (since ?? 0),
                    items = rows
                });
            });
        });
    }

    private static bool Authorized(HttpContext ctx, AiServeOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.ApiKey)) return true;
        return ctx.Request.Headers.TryGetValue("X-Api-Key", out var key)
               && string.Equals(key, opt.ApiKey, StringComparison.Ordinal);
    }
}
