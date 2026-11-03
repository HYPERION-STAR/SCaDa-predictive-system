namespace SCaDaClient.Options;

/// <summary>
/// ai_respond satırlarının karşı bilgisayara HTTP POST ile iletilmesi.
/// appsettings.json > AiForward bölümünden okunur.
/// </summary>
public sealed class AiForwardOptions
{
    public const string SectionName = "AiForward";

    /// <summary>
    /// Hedef endpoint (örn. http://100.x.x.x:9000/ingest).
    /// Boş bırakılırsa gönderim servisi hiç çalışmaz — satırlar
    /// ai_respond'da send_status='pending' olarak birikir, veri kaybı olmaz.
    /// </summary>
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>Bekleyen satırların taranma aralığı (saniye)</summary>
    public int FlushIntervalSeconds { get; set; } = 5;

    /// <summary>Tek turda en fazla kaç satır gönderilir</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>HTTP istek zaman aşımı (saniye)</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Bir satır bu kadar denemede gönderilemezse send_status='failed' olur ve
    /// kuyruğu tıkamaz. Elle 'pending' yapılarak tekrar denenebilir.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Karşı taraf kimlik doğrulama istiyorsa: X-Api-Key başlığı (boşsa gönderilmez)</summary>
    public string ApiKey { get; set; } = string.Empty;
}
