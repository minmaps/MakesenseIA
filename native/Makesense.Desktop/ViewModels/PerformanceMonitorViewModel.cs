using Makesense.Desktop.Services;

namespace Makesense.Desktop.ViewModels;

public sealed class PerformanceMonitorViewModel : ViewModelBase
{
    private double _ramUsedMb;
    private double _vramUsedMb;
    private double _cpuUsagePercent;
    private double _gpuUsagePercent;
    private bool _gpuMetricsAvailable = true;
    private DateTimeOffset _capturedAt;

    public double RamUsedMb
    {
        get => _ramUsedMb;
        private set => SetProperty(ref _ramUsedMb, value);
    }

    public double VramUsedMb
    {
        get => _vramUsedMb;
        private set => SetProperty(ref _vramUsedMb, value);
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set => SetProperty(ref _cpuUsagePercent, value);
    }

    public double GpuUsagePercent
    {
        get => _gpuUsagePercent;
        private set => SetProperty(ref _gpuUsagePercent, value);
    }

    public bool GpuMetricsAvailable
    {
        get => _gpuMetricsAvailable;
        private set
        {
            if (SetProperty(ref _gpuMetricsAvailable, value))
            {
                RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTimeOffset CapturedAt
    {
        get => _capturedAt;
        private set => SetProperty(ref _capturedAt, value);
    }

    public string StatusText => GpuMetricsAvailable
        ? "Échantillonnage du process en temps réel; limites = plafonds non réservés"
        : "GPU/VRAM indisponible sur ce pilote";

    public void Load(PerformanceMonitorSnapshot snapshot)
    {
        RamUsedMb = snapshot.RamUsedMb;
        VramUsedMb = snapshot.VramUsedMb;
        CpuUsagePercent = snapshot.CpuUsagePercent;
        GpuUsagePercent = snapshot.GpuUsagePercent;
        GpuMetricsAvailable = snapshot.GpuMetricsAvailable;
        CapturedAt = snapshot.CapturedAt;
    }
}
