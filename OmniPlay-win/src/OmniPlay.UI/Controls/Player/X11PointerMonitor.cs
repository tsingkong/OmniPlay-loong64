using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace OmniPlay.UI.Controls.Player;

/// <summary>
/// 在 X11 上轮询全局指针位置。Linux 的 mpv 子窗口会吞掉所有指针事件，
/// 播放浮层需要用它来检测鼠标是否落在视频区域，从而显示控制条/窗口按钮。
/// </summary>
public sealed class X11PointerMonitor : IDisposable
{
    private const uint Button1Mask = 1u << 8;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(30);

    private readonly DispatcherTimer timer;
    private IntPtr display;
    private IntPtr targetWindow;
    private uint previousButtonMask;
    private int lastX;
    private int lastY;
    private bool hasLastPosition;
    private bool isRunning;

    public event EventHandler<X11PointerActivityEventArgs>? PointerMoved;

    public event EventHandler? PrimaryButtonPressed;

    public X11PointerMonitor()
    {
        timer = new DispatcherTimer { Interval = DefaultInterval };
        timer.Tick += Timer_OnTick;
    }

    public void Start(IntPtr targetWindow)
    {
        if (isRunning || !OperatingSystem.IsLinux())
        {
            return;
        }

        display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return;
        }

        this.targetWindow = targetWindow;
        previousButtonMask = 0;
        hasLastPosition = false;
        timer.Start();
        isRunning = true;
    }

    public void Stop()
    {
        if (!isRunning)
        {
            return;
        }

        timer.Stop();
        if (display != IntPtr.Zero)
        {
            XCloseDisplay(display);
            display = IntPtr.Zero;
        }

        targetWindow = IntPtr.Zero;
        isRunning = false;
    }

    public void Dispose() => Stop();

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (display == IntPtr.Zero || targetWindow == IntPtr.Zero)
        {
            return;
        }

        // 以主窗口为参照查询指针：只有指针位于主窗口（及其 mpv 子窗口）内时
        // child 才非 None；指针在浮层弹窗或其它窗口上时直接忽略。
        var result = XQueryPointer(
            display,
            targetWindow,
            out var root,
            out var child,
            out var rootX,
            out var rootY,
            out var windowX,
            out var windowY,
            out var mask);

        if (result == 0 || child == IntPtr.Zero)
        {
            return;
        }

        var moved = !hasLastPosition || windowX != lastX || windowY != lastY;
        lastX = windowX;
        lastY = windowY;
        hasLastPosition = true;

        if (moved)
        {
            PointerMoved?.Invoke(
                this,
                new X11PointerActivityEventArgs(windowX, windowY, mask));
        }

        if ((mask & Button1Mask) != 0 && (previousButtonMask & Button1Mask) == 0)
        {
            PrimaryButtonPressed?.Invoke(this, EventArgs.Empty);
        }

        previousButtonMask = mask;
    }

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(string? name);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XQueryPointer")]
    private static extern int XQueryPointer(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int windowXReturn,
        out int windowYReturn,
        out uint maskReturn);
}

public sealed class X11PointerActivityEventArgs : EventArgs
{
    public X11PointerActivityEventArgs(int x, int y, uint buttonMask)
    {
        X = x;
        Y = y;
        ButtonMask = buttonMask;
    }

    /// <summary>相对主窗口原点的 X 坐标（物理像素）。</summary>
    public int X { get; }

    /// <summary>相对主窗口原点的 Y 坐标（物理像素）。</summary>
    public int Y { get; }

    public uint ButtonMask { get; }
}
