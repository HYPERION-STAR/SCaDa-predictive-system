using SCaDaClient.Models.Telemetry;

namespace SCaDaClient.Models;

public sealed record LiveDataSnapshot(
    IReadOnlyDictionary<string, TelemetryBase> Entities,
    string RawJson,
    DateTime CapturedAt);
