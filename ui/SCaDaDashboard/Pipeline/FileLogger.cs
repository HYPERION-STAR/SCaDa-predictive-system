using System.IO;
using Microsoft.Extensions.Logging;

namespace SCaDaDashboard.Pipeline;

/// <summary>
/// Küçük dosya-tabanlı ILogger — Kişi 2'nin istemcisindeki (SCaDaClient)
/// <c>LiveDataService</c>'in KENDİ günlük satırlarını (başlatma, "Canlı veri
/// alındı: N varlık", hata turları) diske yazar. Daha önce NullLogger ile
/// yutuluyordu; bu logger sayesinde verinin gerçekten istemci üzerinden aktığı
/// (ve poll'un çalıştığı/başarısız olduğu) çalışma anında doğrulanabilir.
/// exe klasöründeki <c>SCaDaclient.log</c>'a ekler.
/// </summary>
internal sealed class FileLogger<T> : ILogger<T>
{
    private static readonly object _lock = new();
    private readonly string _path;

    public FileLogger(string path) => _path = path;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;

    public void Log<TState>(
        LogLevel level, EventId id, TState state, Exception? ex,
        Func<TState, Exception?, string> formatter)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {typeof(T).Name}: "
                    + formatter(state, ex)
                    + (ex != null ? $"  :: {ex.GetType().Name}: {ex.Message}" : "");
        lock (_lock) File.AppendAllText(_path, line + Environment.NewLine);
    }
}
