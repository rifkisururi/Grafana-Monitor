namespace Grafana_Monitor.Options;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public int HeavySleepSeconds { get; set; } = 3;

    public string ReportMode { get; set; } = "slow";
}
