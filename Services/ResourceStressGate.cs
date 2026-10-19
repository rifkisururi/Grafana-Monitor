namespace Grafana_Monitor.Services;

public sealed class ResourceStressGate(int maxConcurrentRequests) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(maxConcurrentRequests, maxConcurrentRequests);

    public bool TryEnter() => _slots.Wait(0);

    public void Exit() => _slots.Release();

    public void Dispose() => _slots.Dispose();
}
