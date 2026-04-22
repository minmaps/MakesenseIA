namespace Makesense.Desktop.Services;

public readonly record struct PerformanceMonitorSnapshot(
    double RamUsedMb,
    double VramUsedMb,
    double CpuUsagePercent,
    double GpuUsagePercent,
    bool GpuMetricsAvailable,
    DateTimeOffset CapturedAt);
