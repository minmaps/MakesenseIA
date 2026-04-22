namespace Makesense.Desktop.Services;

public interface IPerformanceMonitorService : IDisposable
{
    PerformanceMonitorSnapshot Capture();
}
