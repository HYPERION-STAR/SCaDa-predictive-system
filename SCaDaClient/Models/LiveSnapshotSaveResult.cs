namespace SCaDaClient.Models;

public sealed record LiveSnapshotSaveResult(
    ulong SnapshotId,
    DateTime CapturedAt,
    uint EntityCount,
    long RetainedSnapshotCount,
    int NarrowTelemetryRows,
    string DatabaseServer,
    uint DatabasePort,
    string DatabaseName);
