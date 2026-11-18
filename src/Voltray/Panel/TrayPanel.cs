using System.Runtime.InteropServices;
using Microsoft.Win32;
using Voltray.Core;
using Voltray.Core.Icons;
using static Voltray.Interop.Win32;
using Vortice.DirectWrite;
using Voltray.Rendering;

namespace Voltray.Panel;

/// One tile of the panel, already resolved (name, icon, level, menu state).
internal sealed record PanelDevice(
    string Id, string Name, string IconPath, DeviceCategory Category, bool CategoryAuto,
    bool Connected, bool Charging, double Fraction, int Step, string Number, bool HasNumber,
    bool InTray, bool Notify, DateTimeOffset? UpdatedAt);

/// What the panel asks of the host. Set once by Program; everything runs on the UI thread.
internal sealed class PanelHost
{
    public required Func<IReadOnlyList<PanelDevice>> Devices { get; init; }
    public required Action Refresh { get; init; }
    public required Action<string?> OpenWindow { get; init; }
    public required Action<string, bool> SetInTray { get; init; }
    public required Action<string, bool> SetNotify { get; init; }
    public required Action<string, DeviceCategory?> SetCategory { get; init; }
}

/// The flyout that opens above the tray on a left click: a 2-column grid of device tiles whose
/// background fills from the bottom up to the battery level. Drawn here with GDI+ on a Windows 11
/// acrylic backdrop, so it opens instantly without starting the WinUI settings process.
internal static unsafe partial class TrayPanel
{
    // Layout, in DIPs
    const float W = 340, HeaderH = 46, Pad = 8, Gap = 6, TileH = 92, FooterH = 40, EmptyH = 64;
    const float TileW = (W - 2 * Pad - Gap) / 2;
    const float Radius = 6, IconSize = 22;

    const nint TimerAnim = 1, TimerSlide = 2, TimerWatch = 3;
    const int SlideMs = 200;

    static PanelHost? _host;
    static nint _hwnd;
    static IReadOnlyList<PanelDevice> _devices = [];
    static float _scale = 1;
    static int _pw, _ph, _x, _y, _slideFrom;
    static long _shownAt, _hiddenAt;
    static bool _light, _backdrop, _animate, _tracking, _menuOpen;
    static int _hover = -1;
    static readonly List<(RectF Rect, Hit Kind, int Index)> Hits = [];

    // Off-screen surface the frame is drawn into, then blitted to the window
    static nint _memDc, _dib, _oldBmp;
    static byte* _bits;

    enum Hit { Refresh, Settings, Open, Tile }

    public static bool IsVisible => _hwnd != 0 && IsWindowVisible(_hwnd);

    public static void Init(PanelHost host)
    {
        _host = host;
        fixed (char* cls = "VoltrayPanel")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, lpszClassName = cls, hCursor = LoadCursorW(0, 32512) };
            RegisterClassEx(ref wc);
        }
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, "VoltrayPanel", "Voltray", WS_POPUP, 0, 0, 0, 0, 0, 0, 0, 0);
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref round, 4);
    }

    /// Blurred, tinted backdrop like the window's DesktopAcrylicBackdrop. The system backdrop
    /// (DWMSBT_TRANSIENTWINDOW) only blurs while the window is active and turned plain grey,
    /// so this uses the composition accent, which blurs whatever is behind at all times.
    static void ApplyBackdrop()
    {
        // ABGR tint: the window's acrylic colours, translucent enough for the blur to show
        uint tint = _light ? 0xB8F3F3F3 : 0xA0202020;
        var accent = new ACCENT_POLICY { AccentState = 4 /* ACCENT_ENABLE_ACRYLICBLURBEHIND */, GradientColor = tint };
        var data = new WINDOWCOMPOSITIONATTRIBDATA { Attrib = 19 /* WCA_ACCENT_POLICY */, Data = &accent, Size = sizeof(ACCENT_POLICY) };
        _backdrop = SetWindowCompositionAttribute(_hwnd, ref data);
    }

    /// Left click on a tray icon: open, or close if it is open (or was closed by this very click).
    public static void Toggle()
    {
        if (_hwnd == 0) return;
        if (IsVisible && _passive) { Activate(); return; } // opened by hovering: a click keeps it open
        if (IsVisible) { Hide(); return; }
        if (Environment.TickCount64 - _hiddenAt < 400) return; // the click that deactivated it
        Show();
    }

    /// Devices or settings changed: redraw if open.
    public static void Update()
    {
        if (!IsVisible || _host is null) return;
        _devices = _host.Devices();
        int h = (int)MathF.Ceiling(HeightDip() * _scale);
        if (h != _ph) Place(animate: false);
        Render();
    }

    /// --panel-preview: opens at a screen point without taking focus (for screenshots while
    /// another app is in front).
    public static void Preview(POINT at, bool? light)
    {
        _anchor = at;
        _themeOverride = light;
        Show(activate: false);
    }

    static POINT? _anchor;
    static bool? _themeOverride;

    // Opened by hovering over the taskbar text: shown without focus, and closed again once the
    // mouse has left both the text and the panel for a moment. A click in it makes it a normal panel.
    static bool _passive;
    static Func<RECT?>? _hoverAnchor;
    static long _outsideSince;

    public static void ShowOnHover(Func<RECT?> anchor)
    {
        if (_hwnd == 0 || IsVisible) return;
        if (Environment.TickCount64 - _hiddenAt < 400) return;
        _hoverAnchor = anchor;
        _passive = true;
        _outsideSince = 0;
        Show(activate: false);
        SetTimer(_hwnd, TimerWatch, 120, 0);
    }

    static void Activate()
    {
        _passive = false;
        KillTimer(_hwnd, TimerWatch);
        SetForegroundWindow(_hwnd);
    }

    static void Watch()
    {
        GetCursorPos(out var p);
        bool inside = Contains(GetRect(_hwnd), p) || (_hoverAnchor?.Invoke() is { } a && Contains(a, p));
        if (inside) { _outsideSince = 0; return; }
        if (_outsideSince == 0) _outsideSince = Environment.TickCount64;
        else if (Environment.TickCount64 - _outsideSince > 350) Hide();
    }

    static RECT GetRect(nint h) { GetWindowRect(h, out var r); return r; }
    static bool Contains(RECT r, POINT p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    static void Show(bool activate = true)
    {
        _devices = _host!.Devices();
        _light = _themeOverride ?? Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
        int dark = _light ? 0 : 1;
        DwmSetWindowAttribute(_hwnd, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE: border colour
        ApplyBackdrop();
        int anim = 1;
        SystemParametersInfoW(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, &anim, 0);
        _animate = anim != 0;
        _hover = -1;
        Place(animate: _animate);
        Render();
        ShowWindow(_hwnd, activate ? 5 /* SW_SHOW */ : 8 /* SW_SHOWNA */);
        if (activate) SetForegroundWindow(_hwnd);
        _shownAt = Environment.TickCount64;
        if (_animate) SetTimer(_hwnd, TimerSlide, 10, 0);
        UpdateAnimTimer();
    }

    static void Hide()
    {
        _passive = false;
        KillTimer(_hwnd, TimerWatch);
        KillTimer(_hwnd, TimerAnim);
        KillTimer(_hwnd, TimerSlide);
        ShowWindow(_hwnd, 0 /* SW_HIDE */);
        _hiddenAt = Environment.TickCount64;
    }

    static float HeightDip()
    {
        int rows = (_devices.Count + 1) / 2;
        float grid = rows == 0 ? EmptyH : rows * TileH + (rows - 1) * Gap;
        return HeaderH + grid + Pad + FooterH;
    }

    /// Sizes the window for the current devices and puts it in the corner of the work area next
    /// to the taskbar, on the monitor where the click happened.
    static void Place(bool animate)
    {
        POINT cur;
        if (_anchor is { } a) cur = a; else GetCursorPos(out cur);
        nint mon = MonitorFromPoint(cur, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
        GetMonitorInfoW(mon, ref mi);
        GetDpiForMonitor(mon, 0, out uint dpi, out _);
        _scale = dpi / 96f;
        var work = mi.rcWork;
        int margin = (int)(12 * _scale);
        _pw = (int)MathF.Ceiling(W * _scale);
        _ph = Math.Min((int)MathF.Ceiling(HeightDip() * _scale), work.Bottom - work.Top - 2 * margin);

        // Which edge the taskbar is on: where the work area is inset from the monitor
        var m = mi.rcMonitor;
        bool top = work.Top > m.Top, left = work.Left > m.Left, right = work.Right < m.Right;
        _x = left ? work.Left + margin : work.Right - margin - _pw;
        _y = top ? work.Top + margin : work.Bottom - margin - _ph;
        if (left || right) _y = Math.Clamp(cur.Y - _ph / 2, work.Top + margin, work.Bottom - margin - _ph);
        _slideFrom = animate ? (int)(24 * _scale) * (top ? -1 : 1) : 0;
        if (left) _slideFrom = 0;
        SetWindowPos(_hwnd, -1 /* HWND_TOPMOST */, _x, _y + _slideFrom, _pw, _ph, SWP_NOACTIVATE);
        CreateSurface();
    }

    static void CreateSurface()
    {
        if (_memDc == 0) _memDc = CreateCompatibleDC(0);
        if (_dib != 0) { SelectObject(_memDc, _oldBmp); DeleteObject(_dib); }
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = _pw, biHeight = -_ph, biPlanes = 1, biBitCount = 32 };
        _dib = CreateDIBSection(_memDc, ref bi, 0, out _bits, 0, 0);
        _oldBmp = SelectObject(_memDc, _dib);
    }

    static void UpdateAnimTimer()
    {
        if (_animate && IsVisible && _devices.Any(d => d.Charging)) SetTimer(_hwnd, TimerAnim, 33, 0);
        else KillTimer(_hwnd, TimerAnim);
    }

    // ---------------------------------------------------------------- drawing

    readonly record struct Palette(uint Tile, uint TileHover, uint Ink, uint Muted, uint Accent, uint Charge, uint Line, uint Hover, uint Fallback, byte FillAlpha);

    static Palette Colors => _light
        ? new(0xB8FFFFFF, 0xFFFFFFFF, 0xFF1A1A1A, 0xFF5F6368, 0xFF005FB8, 0xFF007A74, 0x14000000, 0x0F000000, 0xFFF3F3F3, 0x70)
        : new(0x0DFFFFFF, 0x18FFFFFF, 0xFFFFFFFF, 0xFFC5C5C5, 0xFF60CDFF, 0xFF6FF0E0, 0x14FFFFFF, 0x12FFFFFF, 0xFF2C2C2C, 0x66);

    static readonly uint[] LevelRgb = [0x57D68D, 0xFFD84A, 0xFFA24D, 0xFF7A85]; // green, yellow, orange, red

    static uint Argb(uint rgb, int alpha) => (uint)Math.Clamp(alpha, 0, 255) << 24 | (rgb & 0xFFFFFF);
    static uint Fade(uint argb, float k) => Argb(argb, (int)((argb >> 24) * k));

    static D2DCanvas? _canvas;

    static void Render()
    {
        if (_bits == null) return;
        var g = _canvas ??= new D2DCanvas();
        g.Begin(_memDc, _pw, _ph, _scale);
        Draw(g);
        g.End();
        Present();
    }

    static void Present()
    {
        nint dc = GetDC(_hwnd);
        BitBlt(dc, 0, 0, _pw, _ph, _memDc, 0, 0, 0x00CC0020 /* SRCCOPY */);
        ReleaseDC(_hwnd, dc);
    }

    readonly record struct RectF(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
    }

    static class Fonts
    {
        public static readonly TextStyle Title = new("Segoe UI Variable Display", 14, FontWeight.SemiBold), Small = new("Segoe UI Variable Text", 12),
            Link = new("Segoe UI Variable Text", 13), Number = new("Segoe UI Variable Display", 22, FontWeight.SemiBold),
            Unit = new("Segoe UI Variable Display", 13, FontWeight.SemiBold), Word = new("Segoe UI Variable Display", 16, FontWeight.SemiBold),
            Glyph = new(D2DCanvas.IconFont, 14), Bolt = new(D2DCanvas.IconFont, 12);
    }

    static void Draw(D2DCanvas g)
    {
        var c = Colors;
        Hits.Clear();
        float h = _ph / _scale;
        if (!_backdrop) g.Fill(0, 0, W, h, c.Fallback);

        // Header: title, refresh, settings
        g.Text("Dispositivos", Fonts.Title, 16, 13, 200, c.Ink);
        HeaderButton(g, new RectF(W - Pad - 64, 8, 32, 30), "", Hit.Refresh);
        HeaderButton(g, new RectF(W - Pad - 32, 8, 32, 30), "", Hit.Settings);

        // Tiles
        float footerY = h - FooterH;
        if (_devices.Count == 0)
            g.Text("Nenhum dispositivo conectado", Fonts.Small, 0, HeaderH + 20, W, c.Muted, TextAlignment.Center);
        for (int i = 0; i < _devices.Count; i++)
        {
            float x = Pad + (i % 2) * (TileW + Gap), y = HeaderH + (i / 2) * (TileH + Gap);
            if (y + TileH > footerY - Pad + 0.5f) break; // taller than the screen: the rest is in the window
            bool alone = i == _devices.Count - 1 && i % 2 == 0; // odd count: the last tile takes the whole row
            var r = new RectF(x, y, alone ? W - 2 * Pad : TileW, TileH);
            Hits.Add((r, Hit.Tile, i));
            DrawTile(g, _devices[i], r, _hover == Hits.Count - 1);
        }

        // Footer: last update, open the window
        g.Fill(0, footerY, W, 1 / _scale, c.Line);
        var last = _devices.Select(d => d.UpdatedAt).Where(t => t != null).Max();
        g.Text(last is { } t ? "Atualizado " + Ago(t) : "", Fonts.Small, 16, footerY + 11, 180, c.Muted);
        float lw = D2DCanvas.Measure("Abrir Voltray", Fonts.Link) + 16;
        var link = new RectF(W - Pad - lw, footerY + 6, lw, 28);
        Hits.Add((link, Hit.Open, -1));
        if (_hover == Hits.Count - 1) g.FillRounded(link.X, link.Y, link.Width, link.Height, 4, c.Hover);
        g.Text("Abrir Voltray", Fonts.Link, link.X, footerY + 10.5f, lw, c.Accent, TextAlignment.Center);
    }

    static void HeaderButton(D2DCanvas g, RectF r, string glyph, Hit kind)
    {
        Hits.Add((r, kind, -1));
        if (_hover == Hits.Count - 1) g.FillRounded(r.X, r.Y, r.Width, r.Height, 4, Colors.Hover);
        g.Text(glyph, Fonts.Glyph, r.X, r.Y + 8, r.Width, Colors.Ink, TextAlignment.Center);
    }

    static void DrawTile(D2DCanvas g, PanelDevice d, RectF r, bool hover)
    {
        var c = Colors;
        float k = d.Connected ? 1 : 0.45f;
        g.PushRoundedClip(r.X, r.Y, r.Width, r.Height, Radius);
        g.Fill(r.X, r.Y, r.Width, r.Height, hover ? c.TileHover : c.Tile);

        float top = r.Y + r.Height * (1 - (float)Math.Clamp(d.Fraction, 0, 1)), bottom = r.Y + r.Height;
        if (d.Connected && d.Fraction > 0)
        {
            if (d.Charging) DrawChargingFill(g, r, top);
            else
            {
                // Strongest at the level line, fading out towards the bottom; the top 10 px softened
                uint col = Argb(LevelRgb[d.Step], c.FillAlpha);
                float tip = Math.Min(10, bottom - top);
                g.VerticalGradient(r.X, top + tip, r.Width, bottom - top - tip, col, col & 0xFFFFFF);
                g.VerticalGradient(r.X, top, r.Width, tip, Fade(col, 0.4f), col);
            }
        }

        DrawTileContent(g, d, r, Fade(c.Ink, k), Fade(c.Muted, k), Fade(d.Charging ? c.Charge : c.Ink, k));
        if (d.Charging)
        {
            // Over the teal fill everything is white, as in the window; above it keeps its colours
            // (from below the soft tip: the pale tip is too light for white text)
            float solid = top + Math.Min(10, bottom - top);
            g.PushClip(r.X, solid, r.Width, bottom - solid);
            DrawTileContent(g, d, r, 0xFFFFFFFF, 0xE6FFFFFF, 0xFFFFFFFF);
            g.PopClip();
        }
        g.PopClip();
    }

    /// Icon, charging bolt, name and number of a tile.
    static void DrawTileContent(D2DCanvas g, PanelDevice d, RectF r, uint ink, uint muted, uint numColor)
    {
        g.Icon(d.IconPath, r.X + 12, r.Y + 10, IconSize, ink);
        if (d.Charging) g.Text("", Fonts.Bolt, r.Right - 26, r.Y + 13, 14, numColor, TextAlignment.Center);
        g.Text(d.Name, Fonts.Small, r.X + 12, r.Y + 43, r.Width - 24, muted);
        float baseline = r.Y + r.Height - 10;
        string number = d.Connected ? d.Number : "—";
        if (d.Connected && d.HasNumber)
        {
            g.TextOnBaseline(number, Fonts.Number, r.X + 12, baseline, numColor);
            float w = D2DCanvas.Measure(number, Fonts.Number);
            g.TextOnBaseline("%", Fonts.Unit, r.X + 12 + w + 1, baseline, numColor);
        }
        else g.TextOnBaseline(number, Fonts.Word, r.X + 12, baseline, numColor);
    }

    /// Teal bands rising through the fill, a softer tip and a glow at the level that breathes.
    static void DrawChargingFill(D2DCanvas g, RectF r, float top)
    {
        float bottom = r.Y + r.Height, tip = Math.Min(10, bottom - top);
        double t = _animate ? Environment.TickCount64 / 1000.0 : 0;
        const float period = 170;
        float offset = (float)(t * period / 3.2 % period);
        uint[] full = [0xFF0F7F78, 0xFF1FB5A8, 0xFF0F7F78], soft = [0x660F7F78, 0x661FB5A8, 0x660F7F78];
        g.Bands(r.X, top + tip, r.Width, bottom - top - tip, full, r.Y - offset, period);
        g.Bands(r.X, top, r.Width, tip, soft, r.Y - offset, period);
        float breath = _animate ? (float)(0.65 + 0.35 * Math.Cos(t * 2 * Math.PI / 1.6)) : 0.8f;
        g.VerticalGradient(r.X, top, r.Width, Math.Min(18, bottom - top), Argb(0x7FF5E6, (int)((_light ? 0x50 : 0x80) * breath)), 0x007FF5E6);
    }

    static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        return d.TotalMinutes < 1 ? "agora" : d.TotalHours < 1 ? $"há {(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"há {(int)d.TotalHours} h" : $"em {t:dd/MM}";
    }

    // ---------------------------------------------------------------- input

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    nint dc = BeginPaint(hwnd, ref ps);
                    if (_memDc != 0) BitBlt(dc, 0, 0, _pw, _ph, _memDc, 0, 0, 0x00CC0020);
                    EndPaint(hwnd, ref ps);
                    return 0;
                case 0x0014: // WM_ERASEBKGND
                    return 1;
                case 0x0006 when (wParam & 0xFFFF) != 0 && _passive: // activated by a click: stays open
                    _passive = false;
                    KillTimer(hwnd, TimerWatch);
                    return 0;
                case 0x0113 when wParam == TimerWatch:
                    Watch();
                    return 0;
                case 0x0006 when (wParam & 0xFFFF) == 0: // WM_ACTIVATE, WA_INACTIVE
                    if (!_menuOpen) Hide();
                    return 0;
                case 0x0100 when wParam == 0x1B: // WM_KEYDOWN, Esc
                    Hide();
                    return 0;
                case 0x0200: // WM_MOUSEMOVE
                    if (!_tracking)
                    {
                        var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 2 /* TME_LEAVE */, hwndTrack = hwnd };
                        _tracking = TrackMouseEvent(ref tme);
                    }
                    SetHover(HitAt(lParam));
                    return 0;
                case 0x02A3: // WM_MOUSELEAVE
                    _tracking = false;
                    SetHover(-1);
                    return 0;
                case 0x0202: // WM_LBUTTONUP
                    Click(HitAt(lParam));
                    return 0;
                case 0x0113 when wParam == TimerSlide:
                    Slide();
                    return 0;
                case 0x0113 when wParam == TimerAnim:
                    Render();
                    return 0;
                case 0x02E0: // WM_DPICHANGED: Place() sizes for the monitor itself
                    return 0;
                case 0x001A: // WM_SETTINGCHANGE (theme): picked up on the next open
                    if (IsVisible) Hide();
                    return 0;
            }
        }
        catch (Exception e) { Log.Error("TrayPanel", e); }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static int HitAt(nint lParam)
    {
        float x = (short)(lParam & 0xFFFF) / _scale, y = (short)((lParam >> 16) & 0xFFFF) / _scale;
        for (int i = 0; i < Hits.Count; i++)
        {
            var r = Hits[i].Rect;
            if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return i;
        }
        return -1;
    }

    static void SetHover(int hit)
    {
        if (hit == _hover) return;
        _hover = hit;
        Render();
    }

    static void Slide()
    {
        float t = Math.Min(1, (Environment.TickCount64 - _shownAt) / (float)SlideMs);
        float eased = 1 - MathF.Pow(1 - t, 3);
        int offset = (int)MathF.Round(_slideFrom * (1 - eased));
        SetWindowPos(_hwnd, 0, _x, _y + offset, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (t >= 1) KillTimer(_hwnd, TimerSlide);
    }

    static void Click(int hit)
    {
        if (hit < 0 || hit >= Hits.Count || _host is null) return;
        var (_, kind, index) = Hits[hit];
        switch (kind)
        {
            case Hit.Refresh: _host.Refresh(); break;
            case Hit.Settings: Hide(); _host.OpenWindow("settings"); break;
            case Hit.Open: Hide(); _host.OpenWindow(null); break;
            case Hit.Tile when index < _devices.Count: ShowTileMenu(_devices[index]); break;
        }
    }

    const int CmdTray = 1, CmdNotify = 2, CmdIcon = 3, CmdCategoryAuto = 10, CmdCategoryBase = 20;

    /// The device's options, as in the window's menu. The icon grid lives in the window.
    static void ShowTileMenu(PanelDevice d)
    {
        nint menu = CreatePopupMenu(), cat = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING | (d.InTray ? MF_CHECKED : 0), CmdTray, "Mostrar na bandeja");
            AppendMenu(menu, MF_STRING | (d.Notify ? MF_CHECKED : 0), CmdNotify, "Notificações de bateria");
            AppendMenu(cat, MF_STRING | (d.CategoryAuto ? MF_CHECKED : 0), CmdCategoryAuto, "Automática");
            AppendMenu(cat, MF_SEPARATOR, 0, null);
            foreach (var c in Enum.GetValues<DeviceCategory>())
                AppendMenu(cat, MF_STRING | (!d.CategoryAuto && d.Category == c ? MF_CHECKED : 0), CmdCategoryBase + (int)c, Tray.TrayManager.Label(c));
            AppendMenu(menu, MF_POPUP, cat, "Categoria");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdIcon, "Ícone na bandeja…");

            GetCursorPos(out var p);
            _menuOpen = true;
            int cmd;
            try { cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, p.X, p.Y, 0, _hwnd, 0); }
            finally { _menuOpen = false; }
            switch (cmd)
            {
                case CmdTray: _host!.SetInTray(d.Id, !d.InTray); break;
                case CmdNotify: _host!.SetNotify(d.Id, !d.Notify); break;
                case CmdIcon: Hide(); _host!.OpenWindow(null); break;
                case CmdCategoryAuto: _host!.SetCategory(d.Id, null); break;
                case >= CmdCategoryBase: _host!.SetCategory(d.Id, (DeviceCategory)(cmd - CmdCategoryBase)); break;
            }
            if (cmd == 0 && GetForegroundWindow() != _hwnd) Hide(); // menu dismissed by clicking elsewhere
        }
        finally { DestroyMenu(menu); }
    }

    // ---------------------------------------------------------------- interop

    const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWCOMPOSITIONATTRIBDATA { public int Attrib; public void* Data; public int Size; }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowCompositionAttribute(nint h, ref WINDOWCOMPOSITIONATTRIBDATA data);
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)] struct PAINTSTRUCT { public nint hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public fixed byte rgbReserved[32]; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint h, int attr, ref int value, int size);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(nint mon, int type, out uint dpiX, out uint dpiY);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromPoint(POINT p, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfoW(nint mon, ref MONITORINFO mi);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint h, nint after, int x, int y, int w, int cx, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint h, out RECT r);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint h);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint h, nint dc);
    [LibraryImport("user32.dll")] private static partial nint BeginPaint(nint h, ref PAINTSTRUCT ps);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EndPaint(nint h, ref PAINTSTRUCT ps);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [LibraryImport("user32.dll")] private static partial nint LoadCursorW(nint inst, nint id);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SystemParametersInfoW(uint action, uint param, void* value, uint winIni);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bi, uint usage, out byte* bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
}
