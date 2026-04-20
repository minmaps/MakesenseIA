using System.IO;
using System.Text.Json;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed class PerformanceConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HardwareInfoService _hardwareInfoService;
    private readonly string _settingsPath;

    public PerformanceConfigService(HardwareInfoService hardwareInfoService, string? settingsPath = null)
    {
        _hardwareInfoService = hardwareInfoService;
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Makesense.Native",
            "settings.json");
    }

    public PerformanceConfig LoadOrCreate()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var config = JsonSerializer.Deserialize<PerformanceConfig>(json, Options);
                if (config is not null)
                {
                    return config.Normalize();
                }
            }
        }
        catch
        {
        }

        var defaults = CreateDefaults();
        Save(defaults);
        return defaults;
    }

    public void Save(PerformanceConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(config.Normalize(), Options));
    }

    private PerformanceConfig CreateDefaults()
    {
        var totalRamMb = _hardwareInfoService.GetTotalPhysicalMemoryMb();
        var totalVramMb = _hardwareInfoService.GetDedicatedVramMb();

        return new PerformanceConfig
        {
            MaxRamMb = (uint)Math.Min(totalRamMb / 4UL, 16384UL),
            MaxVramMb = (uint)Math.Min(totalVramMb / 2UL, 8192UL),
            MaxDecodeThreads = (uint)Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
            MaxIoThreads = (uint)Math.Clamp(Environment.ProcessorCount / 4, 1, 4),
            MaxInferenceJobs = 1,
            MaxPrefetchImages = 32
        }.Normalize();
    }
}
