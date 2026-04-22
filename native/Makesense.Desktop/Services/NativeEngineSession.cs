using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Makesense.Desktop.Interop;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed record NativeInferenceResultSummary(
    string ActiveImagePath,
    string ModelPath,
    string TaskName,
    string BackendName,
    string ProviderName,
    string StatusMessage,
    uint SuggestionCount,
    ulong Generation,
    MsInferenceResultCode ResultCode);

public sealed record NativeInferenceSuggestion(
    string Id,
    MsInferenceSuggestionKind Kind,
    float Confidence,
    bool IsVisible,
    string LabelName,
    string SuggestedLabel,
    float RectX,
    float RectY,
    float RectWidth,
    float RectHeight,
    float PointX,
    float PointY);

public sealed record NativeInferenceImageResult(
    NativeInferenceResultSummary Summary,
    IReadOnlyList<NativeInferenceSuggestion> Suggestions);

public sealed class NativeEngineSession : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint AddDllDirectory(string newDirectory);

    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private const uint LoadLibrarySearchUserDirs = 0x00000400;
    private static int _nativeRuntimeConfigured;
    private readonly object _nativeCallGate = new();
    private nint _engineHandle;
    private bool _disposed;

    static NativeEngineSession()
    {
        EnsureNativeRuntimeSearchPathConfigured();
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsAttached => _engineHandle != nint.Zero;

    public void Attach(nint hwnd, int width, int height, PerformanceConfig performanceConfig)
    {
        TryInvoke(() =>
        {
            if (_engineHandle != nint.Zero)
            {
                return MsResultCode.Ok;
            }

            var args = new MsCreateEngineArgs
            {
                TargetHwnd = hwnd,
                Width = (uint)Math.Max(width, 1),
                Height = (uint)Math.Max(height, 1),
                Performance = ToNativeConfig(performanceConfig.Normalize()),
                EnableDebugLayer = 0
            };

            _engineHandle = NativeMethods.ms_create_engine(args);
            return _engineHandle == nint.Zero ? MsResultCode.Error : MsResultCode.Ok;
        }, "Native engine attached.", "Failed to create native engine.");
    }

    public void SetPerformanceLimits(PerformanceConfig performanceConfig)
    {
        Execute(() => NativeMethods.ms_set_performance_limits(_engineHandle, ToNativeConfig(performanceConfig.Normalize())),
            "Performance limits applied.");
    }

    public void OpenProject(string path)
    {
        Execute(() => NativeMethods.ms_open_project(_engineHandle, path), $"Project opened: {Path.GetFileName(path)}");
    }

    public void OpenImages(IEnumerable<string> imagePaths)
    {
        var blob = string.Join('\n', imagePaths);
        Execute(() => NativeMethods.ms_open_images(_engineHandle, blob), "Images registered in native engine.");
    }

    public void SetActiveImage(string imagePath)
    {
        Execute(() => NativeMethods.ms_set_active_image(_engineHandle, imagePath), $"Active image: {Path.GetFileName(imagePath)}");
    }

    public void HandleResize(int width, int height)
    {
        HandleInput(new MsInputEvent
        {
            Type = MsInputEventType.Resize,
            Width = (uint)Math.Max(width, 1),
            Height = (uint)Math.Max(height, 1)
        });
    }

    public void HandleMouseMove(float x, float y)
    {
        HandleInput(new MsInputEvent
        {
            Type = MsInputEventType.MouseMove,
            X = x,
            Y = y
        });
    }

    internal void HandleMouseButton(bool isDown, MsMouseButton button, float x, float y)
    {
        HandleInput(new MsInputEvent
        {
            Type = isDown ? MsInputEventType.MouseDown : MsInputEventType.MouseUp,
            Button = button,
            X = x,
            Y = y
        });
    }

    public void HandleMouseWheel(float delta)
    {
        HandleInput(new MsInputEvent
        {
            Type = MsInputEventType.MouseWheel,
            Delta = delta
        });
    }

    public void Render(int width, int height)
    {
        Execute(() => NativeMethods.ms_render(_engineHandle, new MsRenderFrameArgs
        {
            Width = (uint)Math.Max(width, 1),
            Height = (uint)Math.Max(height, 1)
        }), null, false);
    }

    public void SetViewTransform(double zoom, double panOffsetX, double panOffsetY)
    {
        Execute(() => NativeMethods.ms_set_view_transform(_engineHandle, new MsViewTransform
        {
            Zoom = (float)zoom,
            PanOffsetX = (float)panOffsetX,
            PanOffsetY = (float)panOffsetY
        }), null, false);
    }

    public void RunInference(string modelPath, string taskName)
    {
        var previousGeneration = ReadLatestInferenceSummary()?.Generation ?? 0UL;
        Execute(() => NativeMethods.ms_run_inference(_engineHandle, new MsInferenceRequest
        {
            ModelPath = modelPath,
            TaskName = taskName
        }), $"Inference request submitted: {Path.GetFileName(modelPath)} ({taskName}).");

        WaitForLatestInferenceSummary(previousGeneration, modelPath, taskName);
    }

    public IReadOnlyList<NativeInferenceImageResult> RunInferenceBatch(
        string modelPath,
        string taskName,
        IReadOnlyList<string> imagePaths,
        Action<int, int>? progress = null)
    {
        if (_engineHandle == nint.Zero || imagePaths.Count == 0)
        {
            return Array.Empty<NativeInferenceImageResult>();
        }

        var blob = string.Join('\n', imagePaths);
        MsResultCode result;
        lock (_nativeCallGate)
        {
            result = NativeMethods.ms_run_inference_batch(_engineHandle, new MsInferenceBatchRequest
            {
                ModelPath = modelPath,
                TaskName = taskName,
                ImagePathsBlob = blob
            });
        }

        if (result != MsResultCode.Ok)
        {
            StatusChanged?.Invoke(this, $"Inference batch failed to start. Result={result}.");
            return Array.Empty<NativeInferenceImageResult>();
        }

        MsInferenceBatchStatus status;
        do
        {
            Thread.Sleep(200);
            lock (_nativeCallGate)
            {
                result = NativeMethods.ms_get_inference_batch_status(_engineHandle, out status);
            }

            if (result != MsResultCode.Ok)
            {
                StatusChanged?.Invoke(this, $"Unable to read inference batch status. Result={result}.");
                return Array.Empty<NativeInferenceImageResult>();
            }

            progress?.Invoke((int)status.CompletedCount, (int)status.TotalCount);
        }
        while (status.IsRunning != 0);

        var results = new List<NativeInferenceImageResult>((int)status.ResultCount);
        for (var index = 0U; index < status.ResultCount; index++)
        {
            NativeInferenceResultSummary summary;
            lock (_nativeCallGate)
            {
                result = NativeMethods.ms_get_inference_batch_result_summary(_engineHandle, index, out var nativeSummary);
                summary = ToModel(nativeSummary);
            }

            if (result != MsResultCode.Ok)
            {
                continue;
            }

            var suggestions = ReadBatchInferenceSuggestions(index, summary.SuggestionCount);
            results.Add(new NativeInferenceImageResult(summary, suggestions));
        }

        return results;
    }

    public NativeInferenceResultSummary? ReadLatestInferenceSummary()
    {
        if (_engineHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            MsResultCode result;
            MsInferenceResultSummary summary;
            lock (_nativeCallGate)
            {
                result = NativeMethods.ms_get_latest_inference_summary(_engineHandle, out summary);
            }

            return result == MsResultCode.Ok
                ? new NativeInferenceResultSummary(
                    summary.ActiveImagePath ?? string.Empty,
                    summary.ModelPath ?? string.Empty,
                    summary.TaskName ?? string.Empty,
                    summary.BackendName ?? string.Empty,
                    summary.ProviderName ?? string.Empty,
                    summary.StatusMessage ?? string.Empty,
                    summary.SuggestionCount,
                    summary.Generation,
                    summary.ResultCode)
                : null;
        }
        catch (DllNotFoundException)
        {
            StatusChanged?.Invoke(this, "Makesense.Core.dll not found. Build the native C++ runtime first.");
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            StatusChanged?.Invoke(this, "Native runtime exports are missing or outdated.");
            return null;
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, exception.Message);
            return null;
        }
    }

    public IReadOnlyList<NativeInferenceSuggestion> ReadLatestInferenceSuggestions()
    {
        var summary = ReadLatestInferenceSummary();
        if (summary is null || summary.SuggestionCount == 0)
        {
            return Array.Empty<NativeInferenceSuggestion>();
        }

        var suggestions = new List<NativeInferenceSuggestion>((int)summary.SuggestionCount);
        for (var index = 0U; index < summary.SuggestionCount; index++)
        {
            try
            {
                MsResultCode result;
                MsInferenceSuggestion suggestion;
                lock (_nativeCallGate)
                {
                    result = NativeMethods.ms_get_latest_inference_suggestion(_engineHandle, index, out suggestion);
                }

                if (result != MsResultCode.Ok)
                {
                    break;
                }

                suggestions.Add(ToModel(suggestion));
            }
            catch (DllNotFoundException)
            {
                StatusChanged?.Invoke(this, "Makesense.Core.dll not found. Build the native C++ runtime first.");
                break;
            }
            catch (EntryPointNotFoundException)
            {
                StatusChanged?.Invoke(this, "Native runtime exports are missing or outdated.");
                break;
            }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(this, exception.Message);
                break;
            }
        }

        return suggestions;
    }

    public void ExportAnnotations(string outputPath, string formatName)
    {
        Execute(() => NativeMethods.ms_export_annotations(_engineHandle, new MsExportRequest
        {
            OutputPath = outputPath,
            FormatName = formatName
        }), $"Exported annotations to {outputPath}");
    }

    public void Shutdown()
    {
        if (_engineHandle == nint.Zero)
        {
            return;
        }

        try
        {
            lock (_nativeCallGate)
            {
                NativeMethods.ms_shutdown(_engineHandle);
            }
        }
        catch (DllNotFoundException)
        {
        }
        finally
        {
            _engineHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private static void EnsureNativeRuntimeSearchPathConfigured()
    {
        if (Interlocked.Exchange(ref _nativeRuntimeConfigured, 1) != 0)
        {
            return;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        foreach (var candidate in new[] { baseDirectory, assemblyDirectory }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(candidate!, "Makesense.Core.dll")))
            {
                continue;
            }

            SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs | LoadLibrarySearchUserDirs);
            AddDllDirectory(candidate!);
        }
    }

    private void HandleInput(MsInputEvent inputEvent)
    {
        Execute(() => NativeMethods.ms_handle_input_event(_engineHandle, inputEvent), null, false);
    }

    private void Execute(Func<MsResultCode> nativeCall, string? successStatus, bool reportFailures = true)
    {
        TryInvoke(() =>
        {
            if (_engineHandle == nint.Zero)
            {
                return MsResultCode.NotSupported;
            }

            return nativeCall();
        }, successStatus, reportFailures ? "Native runtime call failed." : null);
    }

    private void TryInvoke(Func<MsResultCode> action, string? successStatus, string? failureStatus = null)
    {
        try
        {
            MsResultCode result;
            lock (_nativeCallGate)
            {
                result = action();
            }

            if (result == MsResultCode.Ok)
            {
                if (!string.IsNullOrWhiteSpace(successStatus))
                {
                    StatusChanged?.Invoke(this, successStatus);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(failureStatus))
            {
                StatusChanged?.Invoke(this, $"{failureStatus} Result={result}.");
            }
        }
        catch (DllNotFoundException)
        {
            StatusChanged?.Invoke(this, "Makesense.Core.dll not found. Build the native C++ runtime first.");
        }
        catch (EntryPointNotFoundException)
        {
            StatusChanged?.Invoke(this, "Native runtime exports are missing or outdated.");
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, exception.Message);
        }
    }

    private static MsPerformanceConfig ToNativeConfig(PerformanceConfig config)
    {
        return new MsPerformanceConfig
        {
            MaxRamMb = config.MaxRamMb,
            MaxVramMb = config.MaxVramMb,
            MaxDecodeThreads = config.MaxDecodeThreads,
            MaxIoThreads = config.MaxIoThreads,
            MaxInferenceJobs = config.MaxInferenceJobs,
            MaxPrefetchImages = config.MaxPrefetchImages
        };
    }

    private void WaitForLatestInferenceSummary(ulong previousGeneration, string modelPath, string taskName)
    {
        if (_engineHandle == nint.Zero)
        {
            return;
        }

        for (var attempt = 0; attempt < 1200; attempt++)
        {
            var summary = ReadLatestInferenceSummary();
            if (summary is not null &&
                string.Equals(summary.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(summary.TaskName, taskName, StringComparison.Ordinal) &&
                (summary.Generation > previousGeneration || summary.ResultCode != MsInferenceResultCode.NotFound))
            {
                return;
            }

            Thread.Sleep(100);
        }

        StatusChanged?.Invoke(this, $"Inference is still running for {Path.GetFileName(modelPath)}. Waiting exceeded 120 seconds.");
    }

    private static NativeInferenceSuggestion ToModel(MsInferenceSuggestion suggestion)
    {
        return new NativeInferenceSuggestion(
            suggestion.Id ?? string.Empty,
            suggestion.Kind,
            suggestion.Confidence,
            suggestion.IsVisible != 0,
            suggestion.LabelName ?? string.Empty,
            suggestion.SuggestedLabel ?? string.Empty,
            suggestion.RectX,
            suggestion.RectY,
            suggestion.RectWidth,
            suggestion.RectHeight,
            suggestion.PointX,
            suggestion.PointY);
    }

    private IReadOnlyList<NativeInferenceSuggestion> ReadBatchInferenceSuggestions(uint resultIndex, uint suggestionCount)
    {
        if (suggestionCount == 0)
        {
            return Array.Empty<NativeInferenceSuggestion>();
        }

        var suggestions = new List<NativeInferenceSuggestion>((int)suggestionCount);
        for (var suggestionIndex = 0U; suggestionIndex < suggestionCount; suggestionIndex++)
        {
            try
            {
                MsResultCode result;
                MsInferenceSuggestion suggestion;
                lock (_nativeCallGate)
                {
                    result = NativeMethods.ms_get_inference_batch_result_suggestion(_engineHandle, resultIndex, suggestionIndex, out suggestion);
                }

                if (result != MsResultCode.Ok)
                {
                    break;
                }

                suggestions.Add(ToModel(suggestion));
            }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(this, exception.Message);
                break;
            }
        }

        return suggestions;
    }

    private static NativeInferenceResultSummary ToModel(MsInferenceResultSummary summary)
    {
        return new NativeInferenceResultSummary(
            summary.ActiveImagePath ?? string.Empty,
            summary.ModelPath ?? string.Empty,
            summary.TaskName ?? string.Empty,
            summary.BackendName ?? string.Empty,
            summary.ProviderName ?? string.Empty,
            summary.StatusMessage ?? string.Empty,
            summary.SuggestionCount,
            summary.Generation,
            summary.ResultCode);
    }
}
