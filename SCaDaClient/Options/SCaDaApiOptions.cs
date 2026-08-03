namespace SCaDaClient.Options;

/// <summary>
/// SCaDa API yapılandırma seçenekleri.
/// appsettings.json > SCaDaApi bölümünden okunur.
/// </summary>
public sealed class SCaDaApiOptions
{
    public const string SectionName = "SCaDaApi";

    /// <summary>API sunucu adresi</summary>
    public string BaseUrl { get; set; } = "http://100.114.223.5:8000";

    /// <summary>Canlı akış tazeleme aralığı (saniye)</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>HTTP istek zaman aşımı (saniye)</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
