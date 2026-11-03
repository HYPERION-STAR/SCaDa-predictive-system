namespace SCaDaClient.Options;

/// <summary>
/// Tahmin servisi (FastAPI /predict) yap─▒land─▒rma se├ºenekleri.
/// appsettings.json > PredictionApi b├Âl├╝m├╝nden okunur.
/// </summary>
public sealed class PredictionApiOptions
{
    public const string SectionName = "PredictionApi";

    /// <summary>Tahmin servisi adresi</summary>
    public string BaseUrl { get; set; } = "http://100.114.223.5:8010";

    /// <summary>HTTP istek zaman a┼ƒ─▒m─▒ (saniye) ÔÇö F1'de 1-2 sn ├Ânerilir (┬º12 D-S1)</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Model servisi aktif mi? false = her ┼ƒey kural tabanl─▒ (F1),
    /// true = kompres├Ârler modele gider (F2). Varsay─▒lan false. (┬º12 D-S1)
    /// </summary>
    public bool UseModelService { get; set; } = false;

    /// <summary>Circuit breaker kapal─▒ e┼ƒik ÔÇö art arda ka├º hata sonra devreye girer</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Circuit breaker recovery s├╝resi (saniye)</summary>
    public int RecoverySeconds { get; set; } = 10;
}
