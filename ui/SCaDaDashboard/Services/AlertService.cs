using System.IO;

namespace SCaDaDashboard.Services;

/// <summary>
/// Uyari servisi. Plan geregi risk esigin altina dusunce "SMS/E-posta" tetiklenir.
/// Gercek SMTP yerine simulasyon: her uyariyi bir log dosyasina yazar
/// (ileride System.Net.Mail.SmtpClient ile gercek e-postaya cevrilebilir).
/// </summary>
public sealed class AlertService
{
    private readonly string _logPath;

    public AlertService()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "alerts.log");
    }

    public string LogPath => _logPath;

    /// <summary>E-posta/SMS gonderimini simule eder ve log dosyasina yazar.</summary>
    public void SendAlert(string machineId, string machineName, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] E-POSTA/SMS -> bakim-ekibi | {machineId} ({machineName}): {message}";
        try
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Loglama basarisiz olsa da UI akisi durmamali.
        }
    }
}
