namespace Grafana_Monitor.Models;

public sealed record OrderDto(long Id, long ProductId, int Quantity, DateTime CreatedAt);
