namespace SCaDaDashboard.ViewModels;

/// <summary>Uyari panelinde gosterilen tek bir olay satiri.</summary>
public sealed class AlertItem
{
    public required string Time { get; init; }
    public required string MachineId { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; } // "Kritik" / "Uyarı" / "Bilgi"
}
