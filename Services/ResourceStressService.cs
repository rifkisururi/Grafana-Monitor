using System.Diagnostics;
using Grafana_Monitor.Models;
using Grafana_Monitor.Options;
using Microsoft.Extensions.Options;

namespace Grafana_Monitor.Services;

public sealed class ResourceStressService
{
    private const string SafetyNote = "Demo-only resource pressure. Concurrent calls are limited by configuration; monitor the host and stop the test if it becomes unresponsive.";
    private readonly DemoOptions _options;
    private readonly ResourceStressGate _gate;

    public ResourceStressService(IOptions<DemoOptions> options, ResourceStressGate gate)
    {
        _options = options.Value;
        _gate = gate;
    }

    public Task<ResourceStressDto> RunMemoryAsync(CancellationToken cancellationToken) =>
        RunAsync("memory", useMemory: true, useCpu: false, cancellationToken);

    public Task<ResourceStressDto> RunCpuAsync(CancellationToken cancellationToken) =>
        RunAsync("cpu", useMemory: false, useCpu: true, cancellationToken);

    public Task<ResourceStressDto> RunCombinedAsync(CancellationToken cancellationToken) =>
        RunAsync("memory+cpu", useMemory: true, useCpu: true, cancellationToken);

    private async Task<ResourceStressDto> RunAsync(
        string workload,
        bool useMemory,
        bool useCpu,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_gate.TryEnter())
        {
            throw new ResourceStressLimitException();
        }

        var durationSeconds = _options.StressDurationSeconds;
        var allocatedMegabytes = useMemory ? _options.StressMemoryMegabytes : 0;
        var timer = Stopwatch.StartNew();
        byte[]? buffer = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (useMemory)
            {
                var byteCount = checked(_options.StressMemoryMegabytes * 1024 * 1024);
                buffer = GC.AllocateUninitializedArray<byte>(byteCount);
                // Touch one byte per memory page so the allocation is physically committed.
                for (var offset = 0; offset < byteCount; offset += Environment.SystemPageSize)
                {
                    buffer[offset] = (byte)(offset / Environment.SystemPageSize);
                }
            }

            if (useCpu)
            {
                await Task.Run(() => BurnCpu(TimeSpan.FromSeconds(durationSeconds), cancellationToken), cancellationToken);
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cancellationToken);
            }

            if (buffer is not null)
            {
                GC.KeepAlive(buffer);
            }

            timer.Stop();
            return new ResourceStressDto(
                workload,
                (int)Math.Round(timer.Elapsed.TotalSeconds),
                allocatedMegabytes,
                _options.StressMaxConcurrentRequests,
                SafetyNote);
        }
        finally
        {
            if (buffer is not null)
            {
                Array.Clear(buffer);
            }

            _gate.Exit();
        }
    }

    private static void BurnCpu(TimeSpan duration, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        ulong value = 0x9E3779B97F4A7C15;
        while (timer.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < 100_000; i++)
            {
                value = (value ^ (value << 7)) + (value >> 3) + (ulong)i;
            }
        }

        GC.KeepAlive(value);
    }
}

public sealed class ResourceStressLimitException : Exception
{
}
