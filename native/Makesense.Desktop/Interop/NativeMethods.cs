using System.Runtime.InteropServices;

namespace Makesense.Desktop.Interop;

internal enum MsInputEventType : uint
{
    None = 0,
    MouseMove = 1,
    MouseDown = 2,
    MouseUp = 3,
    MouseWheel = 4,
    Resize = 5
}

internal enum MsMouseButton : uint
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3
}

public enum MsInferenceSuggestionKind : uint
{
    Rect = 0,
    Point = 1
}

public enum MsInferenceResultCode
{
    Ok = 0,
    Error = 1,
    InvalidArgument = 2,
    NotSupported = 3,
    ResourceLimit = 4,
    NotFound = 5
}

internal enum MsResultCode
{
    Ok = 0,
    Error = 1,
    InvalidArgument = 2,
    NotSupported = 3,
    ResourceLimit = 4,
    NotFound = 5
}

[StructLayout(LayoutKind.Sequential)]
internal struct MsPerformanceConfig
{
    public uint MaxRamMb;
    public uint MaxVramMb;
    public uint MaxDecodeThreads;
    public uint MaxIoThreads;
    public uint MaxInferenceJobs;
    public uint MaxPrefetchImages;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MsCreateEngineArgs
{
    public nint TargetHwnd;
    public uint Width;
    public uint Height;
    public MsPerformanceConfig Performance;
    public byte EnableDebugLayer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MsInputEvent
{
    public MsInputEventType Type;
    public float X;
    public float Y;
    public float Delta;
    public MsMouseButton Button;
    public uint Width;
    public uint Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MsRenderFrameArgs
{
    public uint Width;
    public uint Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MsViewTransform
{
    public float Zoom;
    public float PanOffsetX;
    public float PanOffsetY;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MsInferenceRequest
{
    [MarshalAs(UnmanagedType.LPWStr)]
    public string ModelPath;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string TaskName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MsInferenceResultSummary
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string ActiveImagePath;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string ModelPath;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string TaskName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string BackendName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string ProviderName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string StatusMessage;

    public uint SuggestionCount;
    public ulong Generation;
    public MsInferenceResultCode ResultCode;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MsInferenceSuggestion
{
    public MsInferenceSuggestionKind Kind;
    public float Confidence;
    public byte IsVisible;
    public byte Reserved0;
    public byte Reserved1;
    public byte Reserved2;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
    public string Id;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string LabelName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string SuggestedLabel;

    public float RectX;
    public float RectY;
    public float RectWidth;
    public float RectHeight;
    public float PointX;
    public float PointY;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MsExportRequest
{
    [MarshalAs(UnmanagedType.LPWStr)]
    public string OutputPath;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string FormatName;
}

internal static class NativeMethods
{
    private const string LibraryName = "Makesense.Core";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern nint ms_create_engine(in MsCreateEngineArgs args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_set_performance_limits(nint engine, in MsPerformanceConfig config);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern MsResultCode ms_open_project(nint engine, string projectPath);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern MsResultCode ms_open_images(nint engine, string imagePathsBlob);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern MsResultCode ms_set_active_image(nint engine, string imagePath);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_handle_input_event(nint engine, in MsInputEvent inputEvent);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_render(nint engine, in MsRenderFrameArgs args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_set_view_transform(nint engine, in MsViewTransform transform);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_run_inference(nint engine, in MsInferenceRequest request);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern MsResultCode ms_get_latest_inference_summary(nint engine, out MsInferenceResultSummary summary);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern MsResultCode ms_get_latest_inference_suggestion(nint engine, uint index, out MsInferenceSuggestion suggestion);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern MsResultCode ms_export_annotations(nint engine, in MsExportRequest request);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ms_shutdown(nint engine);
}
