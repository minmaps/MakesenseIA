using System.Management;
using System.Runtime.InteropServices;

namespace Makesense.Desktop.Services;

public sealed class HardwareInfoService
{
    public ulong GetTotalPhysicalMemoryMb()
    {
        return GetPhysicallyInstalledSystemMemory(out var memoryKilobytes)
            ? Math.Max(1024UL, memoryKilobytes / 1024UL)
            : 8192UL;
    }

    public ulong GetDedicatedVramMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
            var maxBytes = 0UL;

            foreach (var item in searcher.Get().OfType<ManagementObject>())
            {
                var value = item["AdapterRAM"];
                if (value is not null && ulong.TryParse(value.ToString(), out var adapterBytes))
                {
                    maxBytes = Math.Max(maxBytes, adapterBytes);
                }
            }

            return maxBytes == 0 ? 8192UL : maxBytes / (1024UL * 1024UL);
        }
        catch
        {
            return 8192UL;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
