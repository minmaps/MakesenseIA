using System.Runtime.InteropServices;
using Makesense.Desktop.Services;
using Makesense.Desktop.Interop;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Tests;

public sealed class InferenceSuggestionServiceTests
{
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [Fact]
    public void NativeEngineSession_WhenUnattached_ReturnsNoInferenceSummary()
    {
        using var session = new NativeEngineSession();

        Assert.Null(session.ReadLatestInferenceSummary());
        Assert.Empty(session.ReadLatestInferenceSuggestions());
    }

    [Fact]
    public void NativeEngineSession_AttachAndOpenImages_UsesRealRepositoryAsset()
    {
        var hwnd = GetConsoleWindow();
        if (hwnd == nint.Zero)
        {
            return;
        }

        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var imagePath = Path.Combine(workspaceRoot, "public", "ico", "main-image-color.png");
        Assert.True(File.Exists(imagePath), $"Expected repo asset at {imagePath}.");

        using var session = new NativeEngineSession();
        session.Attach(hwnd, 640, 480, new PerformanceConfig
        {
            MaxRamMb = 1024,
            MaxVramMb = 1024,
            MaxDecodeThreads = 2,
            MaxIoThreads = 2,
            MaxInferenceJobs = 1,
            MaxPrefetchImages = 4
        });

        Assert.True(session.IsAttached);

        session.OpenImages([imagePath]);
        session.SetActiveImage(imagePath);
        Assert.Null(session.ReadLatestInferenceSummary());
        Assert.Empty(session.ReadLatestInferenceSuggestions());
    }
}
