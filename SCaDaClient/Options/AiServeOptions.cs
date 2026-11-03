namespace SCaDaClient.Options;

/// <summary>
/// ai_respond verisini karşı bilgisayarın ÇEKMESİ için açılan HTTP yayını.
/// appsettings.json > AiServe bölümünden okunur.
///
/// Not: bu "pull" modelidir — AiForward (push, HTTP POST) ile birbirinin
/// alternatifidir; ikisi aynı anda da açık olabilir.
/// </summary>
public sealed class AiServeOptions
{
    public const string SectionName = "AiServe";

    /// <summary>Yayın açık mı</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Dinlenecek adres. 0.0.0.0 = tüm arayüzler (Tailscale dahil) — karşı
    /// bilgisayarın erişebilmesi için gerekli. Sadece bu makineden erişim
    /// isteniyorsa 127.0.0.1 yapın.
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Dinlenecek port</summary>
    public int Port { get; set; } = 9000;

    /// <summary>
    /// Boş değilse, isteklerde X-Api-Key başlığı bu değerle eşleşmeli.
    /// Boşsa kimlik doğrulama yapılmaz (tailnet'e açık olur).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Tek istekte döndürülecek en fazla satır sayısı</summary>
    public int MaxLimit { get; set; } = 500;
}
