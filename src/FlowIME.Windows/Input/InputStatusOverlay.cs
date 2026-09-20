using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowIME.Core.Settings;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public sealed record InputStatusOverlaySnapshot(
    bool Started,
    bool Visible,
    bool Persistent,
    string Label,
    InputStatusOverlaySettings Settings,
    long ShowCount,
    DateTimeOffset? LastShownAt,
    string? LastError);

/// <summary>
/// Small external Win32 overlay used only as an input-state indicator. The window
/// is topmost, click-through, excluded from Alt+Tab and never activates. It does
/// not inject into or render inside the target process, which keeps the gameplay
/// integration deliberately non-invasive.
/// </summary>
public sealed class InputStatusOverlay : IDisposable
{
    private const uint WmAppShow = 0x8000 + 0x4A1;
    private const uint WmAppHide = 0x8000 + 0x4A2;
    private const uint WmPaint = 0x000F;
    private const uint WmTimer = 0x0113;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const uint HideTimerId = 1;
    private const uint AnimationTimerId = 2;
    private const uint AnimationFrameMilliseconds = 15;
    private const double EnterAnimationMilliseconds = 160d;
    private const double ExitAnimationMilliseconds = 120d;
    private static readonly nint HtTransparent = new(-1);
    private static readonly nint MaNoActivate = new(3);

    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const int AnimationTravelLogicalPixels = 6;

    private readonly object _lifecycleSync = new();
    private readonly object _diagnosticSync = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private readonly InputStatusCaretBoundsResolver _caretBoundsResolver;
    private readonly string _windowClassName = $"FlowIME.InputStatusOverlay.{Guid.NewGuid():N}";
    private readonly NativeMethods.WindowProc _windowProc;

    private InputStatusOverlaySettings _settings;
    private Presentation _pendingPresentation = Presentation.Hidden;
    private nint _window;
    private ushort _windowClassAtom;
    private uint _threadId;
    private bool _startRequested;
    private bool _disposed;
    private bool _visible;
    private bool _persistent;
    private string _label = string.Empty;
    private long _showCount;
    private DateTimeOffset? _lastShownAt;
    private string? _lastError;
    private OverlayAnimationState _animationState;
    private long _animationStartedTimestamp;
    private int _restingX;
    private int _restingY;
    private int _animationTravel;
    private byte _targetAlpha = 232;
    private LayeredWindowSurface? _surface;

    public InputStatusOverlay(InputStatusOverlaySettings? initialSettings = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Input status overlay requires Windows.");
        }

        _settings = (initialSettings ?? InputStatusOverlaySettings.Default).Normalize();
        _caretBoundsResolver = new InputStatusCaretBoundsResolver(
            TryGetWin32CaretBounds,
            UiAutomationCaretBoundsProvider.TryGetBounds);
        _windowProc = WindowProcedure;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "FlowIME.InputStatusOverlay"
        };
    }

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startRequested)
            {
                return;
            }

            _startRequested = true;
            _thread.Start();
        }

        if (!_started.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Input status overlay initialization timed out.");
        }

        lock (_diagnosticSync)
        {
            if (_lastError is not null && _window == 0)
            {
                throw new InvalidOperationException(
                    $"Input status overlay initialization failed: {_lastError}");
            }
        }
    }

    public void UpdateSettings(InputStatusOverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = settings.Normalize();
        Volatile.Write(ref _settings, normalized);
        if (!normalized.Enabled)
        {
            Hide();
        }
        else if (Volatile.Read(ref _visible))
        {
            var window = Interlocked.CompareExchange(ref _window, IntPtr.Zero, IntPtr.Zero);
            if (window != 0)
            {
                _ = NativeMethods.PostMessageW(window, WmAppShow, 0, 0);
            }
        }
    }

    public void Show(
        string label,
        nint anchorHwnd,
        bool persistent,
        TimeSpan? duration = null,
        nint focusHwnd = 0,
        TsfProfileSnapshot? inputProfile = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = Volatile.Read(ref _settings);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var normalized = label.Trim();
        if (normalized.Length > 12)
        {
            normalized = normalized[..12];
        }

        var milliseconds = persistent
            ? 0u
            : checked((uint)Math.Clamp(
                (duration ?? TimeSpan.FromMilliseconds(1300)).TotalMilliseconds,
                400,
                5000));
        var resolvedAnchor = InputStatusOverlayPlacement.ResolveAnchor(anchorHwnd, focusHwnd);

        Volatile.Write(
            ref _pendingPresentation,
            new Presentation(normalized, resolvedAnchor, persistent, milliseconds, inputProfile));

        var window = Interlocked.CompareExchange(ref _window, IntPtr.Zero, IntPtr.Zero);
        if (window != 0)
        {
            _ = NativeMethods.PostMessageW(window, WmAppShow, 0, 0);
        }
    }

    public void Hide()
    {
        if (_disposed)
        {
            return;
        }

        Volatile.Write(ref _pendingPresentation, Presentation.Hidden);
        var window = Interlocked.CompareExchange(ref _window, IntPtr.Zero, IntPtr.Zero);
        if (window != 0)
        {
            _ = NativeMethods.PostMessageW(window, WmAppHide, 0, 0);
        }
    }

    public InputStatusOverlaySnapshot GetSnapshot()
    {
        lock (_diagnosticSync)
        {
            return new InputStatusOverlaySnapshot(
                Started: _startRequested &&
                    Interlocked.CompareExchange(ref _window, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero,
                Visible: _visible,
                Persistent: _persistent,
                Label: string.IsNullOrWhiteSpace(_label) ? "none" : _label,
                Settings: Volatile.Read(ref _settings),
                ShowCount: Interlocked.Read(ref _showCount),
                LastShownAt: _lastShownAt,
                LastError: _lastError);
        }
    }

    public void Dispose()
    {
        uint threadId;
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            threadId = _threadId;
        }

        if (threadId != 0)
        {
            _ = NativeMethods.PostThreadMessageW(threadId, User32Native.WmQuit, 0, 0);
        }

        var stopped = !_startRequested ||
            !_thread.IsAlive ||
            _thread.Join(TimeSpan.FromSeconds(2));
        if (stopped)
        {
            _started.Dispose();
        }
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = Kernel32Native.GetCurrentThreadId();
            var module = Kernel32Native.GetModuleHandleW(null);
            if (module == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var windowClass = NativeMethods.WindowClassEx.Create(
                module,
                _windowProc,
                _windowClassName);
            _windowClassAtom = NativeMethods.RegisterClassExW(ref windowClass);
            if (_windowClassAtom == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _window = NativeMethods.CreateWindowExW(
                WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
                _windowClassName,
                string.Empty,
                WsPopup,
                0,
                0,
                80,
                44,
                0,
                0,
                module,
                0);
            if (_window == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _started.Set();

            while (true)
            {
                var result = User32Native.GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                _ = User32Native.TranslateMessage(in message);
                _ = User32Native.DispatchMessageW(in message);
            }
        }
        catch (Exception ex)
        {
            lock (_diagnosticSync)
            {
                _lastError = $"{ex.GetType().Name}:{ex.Message}";
            }

            _started.Set();
        }
        finally
        {
            _surface?.Dispose();
            _surface = null;
            if (_window != 0)
            {
                _ = NativeMethods.DestroyWindow(_window);
                _window = 0;
            }

            if (_windowClassAtom != 0)
            {
                var module = Kernel32Native.GetModuleHandleW(null);
                _ = NativeMethods.UnregisterClassW(_windowClassName, module);
                _windowClassAtom = 0;
            }

            _threadId = 0;
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WmAppShow:
                ShowPendingPresentation(hwnd);
                return 0;
            case WmAppHide:
                HideWindow(hwnd);
                return 0;
            case WmTimer:
                if (wParam == (nuint)HideTimerId)
                {
                    HideWindow(hwnd);
                    return 0;
                }

                if (wParam == (nuint)AnimationTimerId)
                {
                    AdvanceAnimation(hwnd);
                    return 0;
                }

                break;
            case WmPaint:
                ValidatePaint(hwnd);
                return 0;
            case WmNcHitTest:
                return HtTransparent;
            case WmMouseActivate:
                return MaNoActivate;
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowPendingPresentation(nint hwnd)
    {
        var presentation = Volatile.Read(ref _pendingPresentation);
        var settings = Volatile.Read(ref _settings).Normalize();
        if (presentation.IsHidden || !settings.Enabled)
        {
            HideWindow(hwnd);
            return;
        }

        var anchor = presentation.AnchorHwnd != 0
            ? presentation.AnchorHwnd
            : User32Native.GetForegroundWindow();
        var dpi = anchor != 0 ? NativeMethods.GetDpiForWindow(anchor) : 96u;
        if (dpi == 0)
        {
            dpi = 96;
        }

        var scale = dpi / 96d;
        var colorScheme = InputStatusOverlayThemeResolver.Resolve();
        using var brandIcon = presentation.InputProfile is null
            ? null
            : InputMethodBrandIconResolver.TryLoad(
                presentation.InputProfile,
                checked((int)Math.Round(32 * scale)));
        using var frame = InputStatusOverlayRenderer.Render(
            presentation.Label,
            settings.Size,
            dpi,
            opacityPercent: 100,
            colorScheme: colorScheme,
            brandIcon: brandIcon);
        var nextSurface = LayeredWindowSurface.Create(frame);
        var width = frame.Width;
        var height = frame.Height;
        var margin = Math.Max(16, (int)Math.Round(24 * scale));
        var caretGap = Math.Max(8, (int)Math.Round(10 * scale));

        var monitor = User32Native.MonitorFromWindow(
            anchor,
            User32Native.MonitorDefaultToNearest);
        var monitorInfo = User32Native.MonitorInfo.Create();
        if (monitor == 0 || !User32Native.GetMonitorInfoW(monitor, ref monitorInfo))
        {
            nextSurface.Dispose();
            lock (_diagnosticSync)
            {
                _lastError = "monitor-resolution-failed";
            }
            return;
        }

        var work = presentation.Persistent
            ? monitorInfo.Monitor
            : monitorInfo.WorkArea;
        var caretBounds = settings.Position == InputStatusOverlayPosition.Caret
            ? _caretBoundsResolver.Resolve(anchor)
            : null;
        var point = InputStatusOverlayPlacement.Resolve(
            settings.Position,
            work,
            width,
            height,
            margin,
            caretBounds,
            caretGap);
        if (point is null)
        {
            // Do not replace an unavailable caret with an unrelated screen
            // position. Hiding also clears a previously persistent/stale frame.
            nextSurface.Dispose();
            HideWindow(hwnd);
            lock (_diagnosticSync)
            {
                _lastError = "caret-position-unavailable";
            }
            return;
        }

        _ = NativeMethods.KillTimer(hwnd, (nuint)HideTimerId);
        _ = NativeMethods.KillTimer(hwnd, (nuint)AnimationTimerId);
        var wasVisible = _visible;
        _restingX = point.Value.X;
        _restingY = point.Value.Y;
        _animationTravel = Math.Max(3, (int)Math.Round(AnimationTravelLogicalPixels * scale));
        _targetAlpha = checked((byte)Math.Round(255 * settings.OpacityPercent / 100d));
        var initialY = settings.AnimationsEnabled && !wasVisible
            ? _restingY + _animationTravel
            : _restingY;

        var initialAlpha = settings.AnimationsEnabled && !wasVisible
            ? (byte)0
            : _targetAlpha;
        if (!nextSurface.Present(hwnd, _restingX, initialY, initialAlpha))
        {
            nextSurface.Dispose();
            lock (_diagnosticSync)
            {
                _lastError = $"update-layered-window-{Marshal.GetLastPInvokeError()}";
            }
            return;
        }

        _surface?.Dispose();
        _surface = nextSurface;
        _ = NativeMethods.SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoActivate | SwpNoMove | SwpNoSize);

        lock (_diagnosticSync)
        {
            Volatile.Write(ref _visible, true);
            _persistent = presentation.Persistent;
            _label = presentation.Label;
            _lastShownAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }

        if (settings.AnimationsEnabled && !wasVisible)
        {
            _animationState = OverlayAnimationState.Entering;
            _animationStartedTimestamp = Stopwatch.GetTimestamp();
            _ = NativeMethods.ShowWindow(hwnd, SwShowNoActivate);
            _ = NativeMethods.SetTimer(
                hwnd,
                (nuint)AnimationTimerId,
                AnimationFrameMilliseconds,
                0);
        }
        else
        {
            _animationState = OverlayAnimationState.Visible;
            _ = NativeMethods.ShowWindow(hwnd, SwShowNoActivate);
        }

        if (!presentation.Persistent)
        {
            _ = NativeMethods.SetTimer(
                hwnd,
                (nuint)HideTimerId,
                presentation.DurationMilliseconds,
                0);
        }

        Interlocked.Increment(ref _showCount);
    }

    private void HideWindow(nint hwnd)
    {
        _ = NativeMethods.KillTimer(hwnd, (nuint)HideTimerId);
        var settings = Volatile.Read(ref _settings);
        if (Volatile.Read(ref _visible) &&
            settings.AnimationsEnabled &&
            _animationState != OverlayAnimationState.Exiting)
        {
            _animationState = OverlayAnimationState.Exiting;
            _animationStartedTimestamp = Stopwatch.GetTimestamp();
            _ = NativeMethods.SetTimer(
                hwnd,
                (nuint)AnimationTimerId,
                AnimationFrameMilliseconds,
                0);
            return;
        }

        HideImmediately(hwnd);
    }

    private void HideImmediately(nint hwnd)
    {
        _ = NativeMethods.KillTimer(hwnd, (nuint)AnimationTimerId);
        _ = NativeMethods.ShowWindow(hwnd, SwHide);
        _animationState = OverlayAnimationState.Hidden;
        lock (_diagnosticSync)
        {
            Volatile.Write(ref _visible, false);
            _persistent = false;
        }
    }

    private void AdvanceAnimation(nint hwnd)
    {
        var duration = _animationState == OverlayAnimationState.Entering
            ? EnterAnimationMilliseconds
            : ExitAnimationMilliseconds;
        var elapsed = Stopwatch.GetElapsedTime(_animationStartedTimestamp).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / duration, 0d, 1d);
        var eased = CubicBezierEaseOut(progress);

        if (_animationState == OverlayAnimationState.Entering)
        {
            ApplyAnimationFrame(
                hwnd,
                eased,
                _restingY + (int)Math.Round(_animationTravel * (1d - eased)));
            if (progress >= 1d)
            {
                _animationState = OverlayAnimationState.Visible;
                _ = NativeMethods.KillTimer(hwnd, (nuint)AnimationTimerId);
            }
            return;
        }

        if (_animationState == OverlayAnimationState.Exiting)
        {
            ApplyAnimationFrame(
                hwnd,
                1d - eased,
                _restingY + (int)Math.Round(_animationTravel * eased));
            if (progress >= 1d)
            {
                HideImmediately(hwnd);
            }
            return;
        }

        _ = NativeMethods.KillTimer(hwnd, (nuint)AnimationTimerId);
    }

    private void ApplyAnimationFrame(nint hwnd, double opacityProgress, int y)
    {
        var alpha = checked((byte)Math.Clamp(
            (int)Math.Round(_targetAlpha * opacityProgress),
            0,
            byte.MaxValue));
        if (_surface is not null && !_surface.Present(hwnd, _restingX, y, alpha))
        {
            lock (_diagnosticSync)
            {
                _lastError = $"update-layered-window-{Marshal.GetLastPInvokeError()}";
            }
        }
    }

    private static double CubicBezierEaseOut(double progress)
    {
        const double x1 = 0.23;
        const double y1 = 1d;
        const double x2 = 0.32;
        const double y2 = 1d;
        var t = progress;
        for (var index = 0; index < 5; index++)
        {
            var x = CubicBezierCoordinate(t, x1, x2) - progress;
            var derivative = CubicBezierDerivative(t, x1, x2);
            if (Math.Abs(derivative) < 0.0001)
            {
                break;
            }
            t = Math.Clamp(t - (x / derivative), 0d, 1d);
        }
        return CubicBezierCoordinate(t, y1, y2);
    }

    private static double CubicBezierCoordinate(double t, double first, double second)
    {
        var inverse = 1d - t;
        return (3d * inverse * inverse * t * first) +
               (3d * inverse * t * t * second) +
               (t * t * t);
    }

    private static double CubicBezierDerivative(double t, double first, double second)
    {
        var inverse = 1d - t;
        return (3d * inverse * inverse * first) +
               (6d * inverse * t * (second - first)) +
               (3d * t * t * (1d - second));
    }

    private static User32Native.Rect? TryGetWin32CaretBounds(nint anchor)
    {
        if (anchor == 0)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(anchor, out _);
        if (threadId == 0)
        {
            return null;
        }

        var info = new User32Native.GuiThreadInfo
        {
            CbSize = checked((uint)Marshal.SizeOf<User32Native.GuiThreadInfo>())
        };
        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.HwndCaret == 0)
        {
            return null;
        }

        var topLeft = new User32Native.Point
        {
            X = info.RcCaret.Left,
            Y = info.RcCaret.Top
        };
        var bottomRight = new User32Native.Point
        {
            X = info.RcCaret.Right,
            Y = info.RcCaret.Bottom
        };
        if (!NativeMethods.ClientToScreen(info.HwndCaret, ref topLeft) ||
            !NativeMethods.ClientToScreen(info.HwndCaret, ref bottomRight))
        {
            return null;
        }

        return new User32Native.Rect
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = Math.Max(topLeft.X + 1, bottomRight.X),
            Bottom = Math.Max(topLeft.Y + 1, bottomRight.Y)
        };
    }

    private static void ValidatePaint(nint hwnd)
    {
        var paint = NativeMethods.PaintStruct.Create();
        var hdc = NativeMethods.BeginPaint(hwnd, ref paint);
        if (hdc != 0)
        {
            _ = NativeMethods.EndPaint(hwnd, ref paint);
        }
    }

    private sealed class LayeredWindowSurface : IDisposable
    {
        private readonly nint _memoryDc;
        private readonly nint _bitmap;
        private readonly nint _previousBitmap;
        private bool _disposed;

        private LayeredWindowSurface(
            nint memoryDc,
            nint bitmap,
            nint previousBitmap,
            int width,
            int height)
        {
            _memoryDc = memoryDc;
            _bitmap = bitmap;
            _previousBitmap = previousBitmap;
            Width = width;
            Height = height;
        }

        private int Width { get; }
        private int Height { get; }

        internal static LayeredWindowSurface Create(InputStatusOverlayFrame frame)
        {
            var screenDc = NativeMethods.GetDC(0);
            if (screenDc == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            nint memoryDc = 0;
            nint bitmap = 0;
            nint previousBitmap = 0;
            var ownershipTransferred = false;
            try
            {
                memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memoryDc == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                var bitmapInfo = NativeMethods.BitmapInfo.Create(frame.Width, frame.Height);
                bitmap = NativeMethods.CreateDIBSection(
                    screenDc,
                    ref bitmapInfo,
                    NativeMethods.DibRgbColors,
                    out var bits,
                    0,
                    0);
                if (bitmap == 0 || bits == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
                var pixels = new byte[frame.ByteCount];
                Marshal.Copy(frame.Pixels, pixels, 0, pixels.Length);
                Marshal.Copy(pixels, 0, bits, pixels.Length);
                var surface = new LayeredWindowSurface(
                    memoryDc,
                    bitmap,
                    previousBitmap,
                    frame.Width,
                    frame.Height);
                ownershipTransferred = true;
                return surface;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    if (previousBitmap != 0 && memoryDc != 0)
                    {
                        _ = NativeMethods.SelectObject(memoryDc, previousBitmap);
                    }
                    if (bitmap != 0)
                    {
                        _ = NativeMethods.DeleteObject(bitmap);
                    }
                    if (memoryDc != 0)
                    {
                        _ = NativeMethods.DeleteDC(memoryDc);
                    }
                }
                _ = NativeMethods.ReleaseDC(0, screenDc);
            }
        }

        internal bool Present(nint hwnd, int x, int y, byte alpha)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var screenDc = NativeMethods.GetDC(0);
            if (screenDc == 0)
            {
                return false;
            }

            try
            {
                var destination = new NativeMethods.NativePoint(x, y);
                var size = new NativeMethods.NativeSize(Width, Height);
                var source = new NativeMethods.NativePoint(0, 0);
                var blend = NativeMethods.BlendFunction.PerPixel(alpha);
                return NativeMethods.UpdateLayeredWindow(
                    hwnd,
                    screenDc,
                    ref destination,
                    ref size,
                    _memoryDc,
                    ref source,
                    0,
                    ref blend,
                    NativeMethods.UlwAlpha);
            }
            finally
            {
                _ = NativeMethods.ReleaseDC(0, screenDc);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_previousBitmap != 0)
            {
                _ = NativeMethods.SelectObject(_memoryDc, _previousBitmap);
            }
            _ = NativeMethods.DeleteObject(_bitmap);
            _ = NativeMethods.DeleteDC(_memoryDc);
        }
    }

    private enum OverlayAnimationState
    {
        Hidden,
        Entering,
        Visible,
        Exiting
    }

    private sealed record Presentation(
        string Label,
        nint AnchorHwnd,
        bool Persistent,
        uint DurationMilliseconds,
        TsfProfileSnapshot? InputProfile)
    {
        internal static Presentation Hidden { get; } = new(string.Empty, 0, false, 0, null);
        internal bool IsHidden => string.IsNullOrEmpty(Label);
    }

    private static class NativeMethods
    {
        internal const uint DibRgbColors = 0;
        internal const uint UlwAlpha = 0x00000002;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint(int x, int y)
        {
            internal int X = x;
            internal int Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeSize(int width, int height)
        {
            internal int Width = width;
            internal int Height = height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BlendFunction
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;

            internal static BlendFunction PerPixel(byte alpha) => new()
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = 1
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;

            internal static BitmapInfo Create(int width, int height) => new()
            {
                Header = new BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = checked((uint)(width * height * 4))
                }
            };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WindowClassEx
        {
            internal uint CbSize;
            internal uint Style;
            internal WindowProc? WindowProcedure;
            internal int ClassExtra;
            internal int WindowExtra;
            internal nint Instance;
            internal nint Icon;
            internal nint Cursor;
            internal nint BackgroundBrush;
            [MarshalAs(UnmanagedType.LPWStr)] internal string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] internal string? ClassName;
            internal nint SmallIcon;

            internal static WindowClassEx Create(
                nint instance,
                WindowProc windowProcedure,
                string className) =>
                new()
                {
                    CbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
                    WindowProcedure = windowProcedure,
                    Instance = instance,
                    ClassName = className
                };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PaintStruct
        {
            internal nint Hdc;
            internal int Erase;
            internal User32Native.Rect Paint;
            internal int Restore;
            internal int IncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            internal byte[] Reserved;

            internal static PaintStruct Create() =>
                new()
                {
                    Reserved = new byte[32]
                };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClassW(string className, nint instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowExW(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateLayeredWindow(
            nint hwnd,
            nint destinationDc,
            ref NativePoint destination,
            ref NativeSize size,
            nint sourceDc,
            ref NativePoint source,
            uint colorKey,
            ref BlendFunction blend,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint GetDC(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint hwnd, nint hdc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            nint hwnd,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint hwnd, int command);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nuint SetTimer(
            nint hwnd,
            nuint eventId,
            uint intervalMilliseconds,
            nint timerProc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool KillTimer(nint hwnd, nuint eventId);

        [DllImport("user32.dll")]
        internal static extern nint BeginPaint(nint hwnd, ref PaintStruct paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndPaint(nint hwnd, ref PaintStruct paint);


        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetGUIThreadInfo(
            uint threadId,
            ref User32Native.GuiThreadInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(
            nint hwnd,
            ref User32Native.Point point);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateCompatibleDC(nint hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateDIBSection(
            nint hdc,
            ref BitmapInfo bitmapInfo,
            uint usage,
            out nint bits,
            nint section,
            uint offset);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint hdc, nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(nint hdc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessageW(
            uint threadId,
            uint message,
            nuint wParam,
            nint lParam);
    }
}
