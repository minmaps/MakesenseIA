using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels;

public sealed class PerformanceConfigViewModel : ViewModelBase
{
    private uint _maxRamMb;
    private uint _maxVramMb;
    private uint _maxDecodeThreads;
    private uint _maxIoThreads;
    private uint _maxInferenceJobs;
    private uint _maxPrefetchImages;

    public uint MaxRamMb
    {
        get => _maxRamMb;
        set => SetProperty(ref _maxRamMb, value);
    }

    public uint MaxVramMb
    {
        get => _maxVramMb;
        set => SetProperty(ref _maxVramMb, value);
    }

    public uint MaxDecodeThreads
    {
        get => _maxDecodeThreads;
        set => SetProperty(ref _maxDecodeThreads, value);
    }

    public uint MaxIoThreads
    {
        get => _maxIoThreads;
        set => SetProperty(ref _maxIoThreads, value);
    }

    public uint MaxInferenceJobs
    {
        get => _maxInferenceJobs;
        set => SetProperty(ref _maxInferenceJobs, value);
    }

    public uint MaxPrefetchImages
    {
        get => _maxPrefetchImages;
        set => SetProperty(ref _maxPrefetchImages, value);
    }

    public void Load(PerformanceConfig config)
    {
        MaxRamMb = config.MaxRamMb;
        MaxVramMb = config.MaxVramMb;
        MaxDecodeThreads = config.MaxDecodeThreads;
        MaxIoThreads = config.MaxIoThreads;
        MaxInferenceJobs = config.MaxInferenceJobs;
        MaxPrefetchImages = config.MaxPrefetchImages;
    }

    public PerformanceConfig ToModel()
    {
        return new PerformanceConfig
        {
            MaxRamMb = MaxRamMb,
            MaxVramMb = MaxVramMb,
            MaxDecodeThreads = MaxDecodeThreads,
            MaxIoThreads = MaxIoThreads,
            MaxInferenceJobs = MaxInferenceJobs,
            MaxPrefetchImages = MaxPrefetchImages
        }.Normalize();
    }
}
