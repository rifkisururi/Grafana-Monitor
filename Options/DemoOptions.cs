namespace Grafana_Monitor.Options;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public int HeavySleepSeconds { get; set; } = 3;

    public string ReportMode { get; set; } = "slow";

    public int StressMemoryMegabytes { get; set; } = 64;

    public int StressDurationSeconds { get; set; } = 10;

    public int StressMaxConcurrentRequests { get; set; } = 2;
}
