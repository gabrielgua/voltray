using System.Runtime.InteropServices;
using Microsoft.Win32;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Rendering;
using static Voltray.Interop.Win32;

namespace Voltray.Panel;

/// What the taskbar text asks of the host. Set once by Program; everything runs on the UI thread.
internal sealed class TaskbarHost
{
    public required Func<IReadOnlyList<TaskbarDevice>> Devices { get; init; }
    public required Action Click { get; init; }
    public required Action<int, int> RightClick { get; init; }
}

/// "🎧 93%  🖱 45%" drawn on the taskbar, just left of the tray. Like TrafficMonitor, it is a
/// layered window made a child of the taskbar (Shell_TrayWnd), so it moves, hides and auto-hides
/// with it. It never becomes a window of its own: when the taskbar is not ready (at sign-in) it
/// waits and retries, and it hides whenever the taskbar is off screen or an app is full screen.
/// Windows does not support this officially, so the tray keeps one Voltray icon as a way back.
internal static unsafe partial class TaskbarWidget
{
    const nint TimerFollow = 1, TimerHover = 2, TimerRetry = 3;

    static TaskbarHost? _host;
    static nint _hwnd, _taskbar, _timerWnd;
    static bool _enabled, _hover, _tracking, _classRegistered, _waitingLogged;
    static TaskbarStyle _style;
    static string _lastKey = "";
    static RECT _shownRect;

    public static void Init(TaskbarHost host) => _host = host;

    /// Turns the text on or off and redraws it (settings or devices changed).
    public static void Sync(Settings settings)
    {
        bool want = settings.Display == DisplayMode.Taskbar;
        _style = settings.TaskbarStyle;
        if (want != _enabled)
        {
            _enabled = want;
            if (!want) Destroy();
        }
        if (_enabled) Refresh(force: true);
    }

    /// Explorer restarted: the old taskbar (and our child window with it) is gone.
    public static void Recreate()
    {
        if (!_enabled) return;
        Destroy();
        Refresh(force: true);
    }

    /// Screen rectangle of the text, for the panel's hover logic.
    public static RECT? Bounds => _hwnd != 0 && IsWindowVisible(_hwnd) ? _shownRect : null;

    static void Destroy()
    {
        if (_hwnd != 0) DestroyWindow(_hwnd);
        _hwnd = 0;
        _lastKey = "";
        if (_timerWnd != 0) KillTimer(_timerWnd, TimerRetry);
    }

    static void RegisterClass()
    {
        if (_classRegistered) return;
        fixed (char* cls = "VoltrayTaskbar")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, lpszClassName = cls, hCursor = LoadCursorW(0, 32512) };
            RegisterClassEx(ref wc);
        }
        _classRegistered = true;
    }

    /// Attaches to the taskbar, or arms a retry: right after sign-in the taskbar exists before it
    /// accepts children, and a window of our own would float over full-screen apps.
    static void EnsureWindow()
    {
        if (_hwnd != 0 && IsWindow(_hwnd) && GetParent(_hwnd) == _taskbar) return;
        if (_hwnd != 0 && IsWindow(_hwnd)) DestroyWindow(_hwnd); // the taskbar was replaced
        _hwnd = 0;
        RegisterClass();
        if (_timerWnd == 0) _timerWnd = CreateWindowEx(0, "VoltrayTaskbar", "Voltray timer", 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, 0, 0);

        _taskbar = FindWindow("Shell_TrayWnd", null);
        if (_taskbar != 0)
            _hwnd = CreateChild(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "VoltrayTaskbar", "Voltray", WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 1, 1, _taskbar, 0, 0, 0);
        if (_hwnd == 0)
        {
            string why = _taskbar == 0 ? "not found" : $"error {Marshal.GetLastPInvokeError()}";
            if (!_waitingLogged) Log.Info($"taskbar: not ready yet ({why}), retrying");
            _waitingLogged = true;
            SetTimer(_timerWnd, TimerRetry, 2000, 0);
            return;
        }
        _waitingLogged = false;
        KillTimer(_timerWnd, TimerRetry);
        Log.Info("taskbar: attached next to the tray");
        SetTimer(_hwnd, TimerFollow, 500, 0); // the tray grows as icons come; full-screen apps come and go
    }

    /// The taskbar is not on screen: auto-hidden (slid off its monitor), or covered by an app in
    /// full screen on that monitor (a game, a video), where the taskbar itself steps aside.
    static bool TaskbarHidden(RECT tb)
    {
        nint mon = MonitorFromWindow(_taskbar, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
        if (!GetMonitorInfoW(mon, ref mi)) return false;
        var m = mi.rcMonitor;
        int visibleW = Math.Min(tb.Right, m.Right) - Math.Max(tb.Left, m.Left);
        int visibleH = Math.Min(tb.Bottom, m.Bottom) - Math.Max(tb.Top, m.Top);
        bool horizontal = tb.Right - tb.Left >= tb.Bottom - tb.Top;
        if (visibleW <= 0 || visibleH <= 0) return true;
        if (horizontal ? visibleH * 2 < tb.Bottom - tb.Top : visibleW * 2 < tb.Right - tb.Left) return true;

        nint fg = GetForegroundWindow();
        if (fg == 0 || fg == _taskbar || !GetWindowRect(fg, out var r)) return false;
        if (MonitorFromWindow(fg, 2) != mon) return false;
        char* cls = stackalloc char[32];
        int n = GetClassNameW(fg, cls, 32);
        string name = new(cls, 0, Math.Max(0, n));
        if (name is "Progman" or "WorkerW") return false; // the desktop covers the monitor too
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// Redraws when anything that changes the picture or the place changed (or always, when forced).
    static void Refresh(bool force)
    {
        if (!_enabled || _host is null) return;
        EnsureWindow();
        if (_hwnd == 0) return;

        var devices = _host.Devices();
        GetWindowRect(_taskbar, out var tb);
        nint trayWnd = FindWindowEx(_taskbar, 0, "TrayNotifyWnd", null);
        RECT tray = default;
        bool hasTray = trayWnd != 0 && GetWindowRect(trayWnd, out tray) && tray.Right > tray.Left;
        uint dpi = GetDpiForWindow(_taskbar);
        bool hidden = TaskbarHidden(tb);
        bool light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

        string key = $"{string.Join(";", devices)}|{_style}|{light}|{dpi}|{tb.Left},{tb.Top},{tb.Right},{tb.Bottom}|{tray.Left}|{_hover}|{hidden}";
        if (!force && key == _lastKey) return;
        _lastKey = key;

        if (devices.Count == 0 || hidden) { ShowWindow(_hwnd, 0); return; }

        float scale = Math.Max(1, dpi) / 96f;
        int height = tb.Bottom - tb.Top;
        var frame = TaskbarRenderer.Render(devices, _style, light, scale, height, _hover);

        // Fully transparent pixels let clicks through to the taskbar; alpha 1 keeps the whole strip
        // hoverable (no flicker between devices) and is invisible.
        var px = frame.Pixels;
        for (int i = 3; i < px.Length; i += 4) if (px[i] == 0) px[i] = 1;

        int margin = (int)(2 * scale);
        int right = hasTray ? tray.Left - margin : tb.Right - (int)(220 * scale);
        int x = right - frame.Width, y = tb.Top;
        _shownRect = new RECT { Left = x, Top = y, Right = x + frame.Width, Bottom = y + frame.Height };
        SetWindowPos(_hwnd, 0 /* HWND_TOP: above the taskbar's XAML layer */, x - tb.Left, 0, frame.Width, frame.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Present(frame);
    }

    static void Present(TaskbarFrame frame)
    {
        nint screen = GetDC(0), mem = CreateCompatibleDC(screen);
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = frame.Width, biHeight = -frame.Height, biPlanes = 1, biBitCount = 32 };
        nint dib = CreateDIBSection(mem, ref bi, 0, out byte* bits, 0, 0);
        nint old = SelectObject(mem, dib);
        try
        {
            frame.Pixels.AsSpan().CopyTo(new Span<byte>(bits, frame.Pixels.Length));
            var size = new SIZE { cx = frame.Width, cy = frame.Height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            if (!UpdateLayeredWindow(_hwnd, screen, 0, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */))
                Log.Error($"taskbar: UpdateLayeredWindow failed ({Marshal.GetLastPInvokeError()})");
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0113 when wParam == TimerRetry && hwnd == _timerWnd:
                    Refresh(force: true);
                    return 0;
                case 0x0113 when wParam == TimerFollow: // WM_TIMER
                    Refresh(force: false);
                    return 0;
                case 0x0113 when wParam == TimerHover:
                    KillTimer(hwnd, TimerHover);
                    if (_hover) TrayPanel.ShowOnHover(() => Bounds);
                    return 0;
                case 0x0200: // WM_MOUSEMOVE
                    if (!_tracking)
                    {
                        var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 2 /* TME_LEAVE */, hwndTrack = hwnd };
                        _tracking = TrackMouseEvent(ref tme);
                    }
                    if (!_hover)
                    {
                        _hover = true;
                        Refresh(force: true);
                        SetTimer(hwnd, TimerHover, 450, 0); // a short pause, so passing over it does not pop the panel
                    }
                    return 0;
                case 0x02A3: // WM_MOUSELEAVE
                    _tracking = false;
                    _hover = false;
                    KillTimer(hwnd, TimerHover);
                    Refresh(force: true);
                    return 0;
                case 0x0202: // WM_LBUTTONUP
                    KillTimer(hwnd, TimerHover);
                    _host?.Click();
                    return 0;
                case 0x0205: // WM_RBUTTONUP
                    KillTimer(hwnd, TimerHover);
                    GetCursorPos(out var p);
                    _host?.RightClick(p.X, p.Y);
                    return 0;
                case 0x0021: // WM_MOUSEACTIVATE: never take focus from the app the user is in
                    return 3; // MA_NOACTIVATE
                case 0x0002: // WM_DESTROY (also when Explorer restarts and takes the taskbar with it)
                    if (hwnd == _hwnd) _hwnd = 0;
                    return 0;
            }
        }
        catch (Exception e) { Log.Error("TaskbarWidget", e); }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ---- interop

    const uint WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_CLIPSIBLINGS = 0x04000000;
    const uint WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateChild(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint h, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint mon, ref MONITORINFO mi);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static partial int GetClassNameW(nint h, char* buf, int max);
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint h, out RECT r);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint h);
    [LibraryImport("user32.dll")] private static partial nint GetParent(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint h);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint h, nint after, int x, int y, int w, int cx, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [LibraryImport("user32.dll")] private static partial nint LoadCursorW(nint inst, nint id);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint h);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint h, nint dc);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint h, nint dst, nint ptDst, ref SIZE size, nint src, ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bi, uint usage, out byte* bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
}
