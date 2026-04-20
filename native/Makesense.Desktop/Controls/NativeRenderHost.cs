using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Makesense.Desktop.Interop;
using Makesense.Desktop.Services;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Controls;

public sealed class NativeRenderHost : HwndHost
{
    private readonly DispatcherTimer _renderTimer;
    private nint _hostHandle;

    public NativeRenderHost()
    {
        Focusable = true;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) => Session?.Render((int)Math.Max(ActualWidth, 1), (int)Math.Max(ActualHeight, 1));
    }

    public NativeEngineSession? Session { get; set; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHandle = Win32.CreateWindowEx(
            0,
            "static",
            string.Empty,
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN,
            0,
            0,
            Math.Max((int)ActualWidth, 1),
            Math.Max((int)ActualHeight, 1),
            hwndParent.Handle,
            nint.Zero,
            nint.Zero,
            nint.Zero);

        Session?.Attach(_hostHandle, (int)Math.Max(ActualWidth, 1), (int)Math.Max(ActualHeight, 1), DefaultConfig());
        _renderTimer.Start();
        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _renderTimer.Stop();
        Session?.Shutdown();
        Win32.DestroyWindow(hwnd.Handle);
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x0005:
                Session?.HandleResize(LowWord(lParam), HighWord(lParam));
                break;
            case 0x0200:
                Session?.HandleMouseMove(SignedLowWord(lParam), SignedHighWord(lParam));
                break;
            case 0x0201:
                Session?.HandleMouseButton(true, MsMouseButton.Left, SignedLowWord(lParam), SignedHighWord(lParam));
                Win32.SetFocus(hwnd);
                break;
            case 0x0202:
                Session?.HandleMouseButton(false, MsMouseButton.Left, SignedLowWord(lParam), SignedHighWord(lParam));
                break;
            case 0x020A:
                Session?.HandleMouseWheel((short)((wParam.ToInt64() >> 16) & 0xffff));
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_hostHandle != nint.Zero)
        {
            Win32.MoveWindow(_hostHandle, 0, 0, Math.Max((int)rcBoundingBox.Width, 1), Math.Max((int)rcBoundingBox.Height, 1), true);
        }
    }

    private static PerformanceConfig DefaultConfig()
    {
        return new PerformanceConfig
        {
            MaxRamMb = 4096,
            MaxVramMb = 2048,
            MaxDecodeThreads = 4,
            MaxIoThreads = 2,
            MaxInferenceJobs = 1,
            MaxPrefetchImages = 32
        };
    }

    private static int LowWord(nint value) => unchecked((ushort)(value.ToInt64() & 0xffff));

    private static int HighWord(nint value) => unchecked((ushort)((value.ToInt64() >> 16) & 0xffff));

    private static short SignedLowWord(nint value) => unchecked((short)(value.ToInt64() & 0xffff));

    private static short SignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xffff));
}
