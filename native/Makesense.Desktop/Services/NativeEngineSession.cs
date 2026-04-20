using System.IO;
using Makesense.Desktop.Interop;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed record NativeInferenceResultSummary(
    string ActiveImagePath,
    string ModelPath,
    string TaskName,
    uint SuggestionCount,
    ulong Generation);

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

public sealed class NativeEngineSession : IDisposable
{
    private nint _engineHandle;
    private bool _disposed;

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

    public NativeInferenceResultSummary? ReadLatestInferenceSummary()
    {
        if (_engineHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var result = NativeMethods.ms_get_latest_inference_summary(_engineHandle, out var summary);
            return result == MsResultCode.Ok
                ? new NativeInferenceResultSummary(
                    summary.ActiveImagePath ?? string.Empty,
                    summary.ModelPath ?? string.Empty,
                    summary.TaskName ?? string.Empty,
                    summary.SuggestionCount,
                    summary.Generation)
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
                var result = NativeMethods.ms_get_latest_inference_suggestion(_engineHandle, index, out var suggestion);
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
            NativeMethods.ms_shutdown(_engineHandle);
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
            var result = action();
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

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var summary = ReadLatestInferenceSummary();
            if (summary is not null &&
                summary.Generation > previousGeneration &&
                string.Equals(summary.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(summary.TaskName, taskName, StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(20);
        }
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
}
