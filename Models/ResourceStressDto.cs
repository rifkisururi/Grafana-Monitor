namespace Grafana_Monitor.Models;

public sealed record ResourceStressDto(
    string Workload,
    int DurationSeconds,
    int AllocatedMegabytes,
    int MaxConcurrentRequests,
    string Note);
