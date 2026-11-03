namespace SCaDaClient.Options;

/// <summary>
/// Model servisinin /hub_state ucu (Kişi 1) yapılandırma seçenekleri.
/// appsettings.json > AiHub bölümünden okunur.
/// </summary>
public sealed class AiHubOptions
{
    public const string SectionName = "AiHub";

    /// <summary>Model servisi adresi</summary>
    public string BaseUrl { get; set; } = "http://100.123.105.25:8010";

    /// <summary>/hub_state çekme aralığı (saniye)</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>HTTP istek zaman aşımı (saniye)</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// true ise gövde bir öncekiyle aynı olduğunda (sha256 eşit) yeni satır
    /// yazılmaz — model 5 sn'den seyrek güncelleniyorsa log şişmez.
    /// </summary>
    public bool SkipUnchanged { get; set; } = true;

    /// <summary>health bu değerin altındaysa düğüm kritik sayılır</summary>
    public double CriticalHealthThreshold { get; set; } = 20.0;
}
