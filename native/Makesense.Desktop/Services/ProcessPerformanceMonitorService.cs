using System.Diagnostics;
using System.Management;

namespace Makesense.Desktop.Services;

public sealed class ProcessPerformanceMonitorService : IPerformanceMonitorService
{
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    private readonly Process _process;
    private readonly int _processId;
    private readonly int _processorCount;
    private DateTimeOffset? _previousCpuSampleTime;
    private TimeSpan? _previousTotalProcessorTime;
    private bool _gpuMetricsAvailable = true;

    public ProcessPerformanceMonitorService()
        : this(Process.GetCurrentProcess())
    {
    }

    internal ProcessPerformanceMonitorService(Process process)
    {
        _process = process;
        _processId = process.Id;
        _processorCount = Math.Max(1, Environment.ProcessorCount);
    }

    public PerformanceMonitorSnapshot Capture()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var ramUsedMb = 0.0;
        var cpuUsagePercent = 0.0;

        try
        {
            _process.Refresh();
            ramUsedMb = _process.WorkingSet64 / BytesPerMegabyte;

            var totalProcessorTime = _process.TotalProcessorTime;
            if (_previousCpuSampleTime is { } previousCpuSampleTime &&
                _previousTotalProcessorTime is { } previousTotalProcessorTime)
            {
                var elapsedMilliseconds = (capturedAt - previousCpuSampleTime).TotalMilliseconds;
                var cpuMilliseconds = (totalProcessorTime - previousTotalProcessorTime).TotalMilliseconds;
                if (elapsedMilliseconds > 0 && cpuMilliseconds >= 0)
                {
                    cpuUsagePercent = Math.Clamp(cpuMilliseconds / elapsedMilliseconds / _processorCount * 100.0, 0.0, 100.0);
                }
            }

            _previousCpuSampleTime = capturedAt;
            _previousTotalProcessorTime = totalProcessorTime;
        }
        catch
        {
            ramUsedMb = 0.0;
            cpuUsagePercent = 0.0;
        }

        var gpuMetrics = CaptureGpuMetrics();
        return new PerformanceMonitorSnapshot(
            ramUsedMb,
            gpuMetrics.VramUsedMb,
            cpuUsagePercent,
            gpuMetrics.GpuUsagePercent,
            gpuMetrics.Available,
            capturedAt);
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    private GpuMetrics CaptureGpuMetrics()
    {
        if (!_gpuMetricsAvailable)
        {
            return new GpuMetrics(0.0, 0.0, false);
        }

        try
        {
            var vramUsedMb = CaptureDedicatedGpuMemoryMb();
            var gpuUsagePercent = CaptureGpuUsagePercent();
            return new GpuMetrics(vramUsedMb, gpuUsagePercent, true);
        }
        catch (ManagementException)
        {
            _gpuMetricsAvailable = false;
            return new GpuMetrics(0.0, 0.0, false);
        }
        catch (UnauthorizedAccessException)
        {
            _gpuMetricsAvailable = false;
            return new GpuMetrics(0.0, 0.0, false);
        }
        catch (Exception)
        {
            _gpuMetricsAvailable = false;
            return new GpuMetrics(0.0, 0.0, false);
        }
    }

    private double CaptureDedicatedGpuMemoryMb()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2",
            "SELECT Name, DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory");
        var dedicatedBytes = 0UL;

        foreach (var item in searcher.Get().OfType<ManagementObject>())
        {
            if (!IsCurrentProcessGpuCounter(item))
            {
                continue;
            }

            dedicatedBytes += ReadUInt64(item, "DedicatedUsage");
        }

        return dedicatedBytes / BytesPerMegabyte;
    }

    private double CaptureGpuUsagePercent()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2",
            "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
        var usagePercent = 0.0;

        foreach (var item in searcher.Get().OfType<ManagementObject>())
        {
            if (!IsCurrentProcessGpuCounter(item))
            {
                continue;
            }

            usagePercent += ReadDouble(item, "UtilizationPercentage");
        }

        return Math.Clamp(usagePercent, 0.0, 100.0);
    }

    private bool IsCurrentProcessGpuCounter(ManagementBaseObject item)
    {
        var name = item["Name"]?.ToString();
        return !string.IsNullOrWhiteSpace(name) &&
            (name.Contains($"pid_{_processId}_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith($"{_processId}_", StringComparison.OrdinalIgnoreCase));
    }

    private static ulong ReadUInt64(ManagementBaseObject item, string propertyName)
    {
        var value = item[propertyName];
        return value is null || !ulong.TryParse(value.ToString(), out var parsed) ? 0UL : parsed;
    }

    private static double ReadDouble(ManagementBaseObject item, string propertyName)
    {
        var value = item[propertyName];
        return value is null || !double.TryParse(value.ToString(), out var parsed) ? 0.0 : parsed;
    }

    private readonly record struct GpuMetrics(double VramUsedMb, double GpuUsagePercent, bool Available);
}
