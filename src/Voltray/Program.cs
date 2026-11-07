using System.Diagnostics;
using System.Runtime.InteropServices;
using Voltray.Core;
using Voltray.Providers.Bluetooth;
using Voltray.Providers.Logitech;
using Voltray.Tray;
using static Voltray.Interop.Win32;

namespace Voltray;

/// Background host: owns the providers, the device registry and the tray icons.
/// Pure Win32 + NativeAOT so it idles at a few MB; the settings UI will be a separate process.
internal static class Program
{
    const uint WM_DEVICES_CHANGED = WM_APP + 1, WM_TRAY_BASE = WM_APP + 0x10;
    const nint TimerRefresh = 1;
    const int CmdRefresh = 1, CmdExit = 2, CmdStylePercentage = 3, CmdStyleGlyph = 4, CmdHideCategory = 5, CmdShowAll = 6;

    static nint _hwnd;
    static uint _taskbarCreated;
    static int _updatePending;
    static Settings _settings = new();
    static readonly DeviceRegistry Registry = new();
    static readonly List<IBatteryProvider> Providers = [new LogitechProvider(), new BluetoothProvider()];
    static TrayManager? _tray;

    [STAThread]
    static unsafe int Main(string[] args)
    {
        if (args is ["--icon-preview", var path]) { IconRenderer.WritePreview(path); return 0; }

        using var single = new Mutex(true, @"Local\Voltray", out bool first);
        if (!first) return 0;

        Log.Info($"Voltray {typeof(Program).Assembly.GetName().Version} starting");
        _settings = Settings.Load();
        foreach (var (id, category) in _settings.CategoryOverrides) Registry.OverrideCategory(id, category);

        fixed (char* cls = "VoltrayTray")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, lpszClassName = cls };
            RegisterClassEx(ref wc);
        }
        _hwnd = CreateWindowEx(0, "VoltrayTray", "Voltray", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _tray = new TrayManager(_hwnd, WM_TRAY_BASE); // icons appear as devices are found

        // Providers report from background threads; coalesce into one repaint on the UI thread.
        Registry.Changed += () => { if (Interlocked.Exchange(ref _updatePending, 1) == 0) PostMessage(_hwnd, WM_DEVICES_CHANGED, 0, 0); };
        _ = Task.Run(StartProvidersAsync);
        SetTimer(_hwnd, TimerRefresh, (uint)RefreshInterval().TotalMilliseconds, 0);

        while (GetMessage(out var msg, 0, 0, 0) > 0) { TranslateMessage(msg); DispatchMessage(msg); }

        _tray.RemoveAll();
        foreach (var p in Providers) p.DisposeAsync().AsTask().Wait(1000);
        return 0;
    }

    static TimeSpan RefreshInterval()
    {
        var wanted = TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes));
        var floor = Providers.Max(p => p.MinimumPollInterval);
        return wanted > floor ? wanted : floor;
    }

    static async Task StartProvidersAsync()
    {
        // In parallel: a sleeping Logitech device must not delay Bluetooth, and vice versa.
        await Task.WhenAll(Providers.Select(async p =>
        {
            try { await p.StartAsync(Registry, CancellationToken.None); }
            catch (Exception e) { Log.Error($"provider {p.Id} failed to start", e); }
        }));
        PostMessage(_hwnd, WM_DEVICES_CHANGED, 0, 0); // shows the "no devices" icon if nothing was found
    }

    static async Task RefreshAsync()
    {
        foreach (var p in Providers)
        {
            try { await p.RefreshAsync(CancellationToken.None); }
            catch (Exception e) { Log.Error($"provider {p.Id} refresh failed", e); }
        }
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == _taskbarCreated && msg != 0 && _tray != null) { _tray.Recreate(Registry.Snapshot(), _settings); return 0; } // Explorer restarted
            if (_tray is null) return DefWindowProc(hwnd, msg, wParam, lParam); // messages sent during CreateWindowEx
            if (_tray.TryGetCategory(msg, out var category))
            {
                uint ev = (uint)(lParam & 0xFFFF);
                if (ev is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT)
                    ShowMenu(category, (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return 0;
            }
            switch (msg)
            {
                case WM_DEVICES_CHANGED:
                    Interlocked.Exchange(ref _updatePending, 0);
                    _tray.Update(Registry.Snapshot(), _settings);
                    return 0;
                case WM_TIMER:
                    _ = Task.Run(RefreshAsync);
                    return 0;
                case WM_SETTINGCHANGE or WM_DPICHANGED: // light/dark taskbar or scale changed: redraw
                    _tray.Update(Registry.Snapshot(), _settings);
                    break;
                case WM_DESTROY:
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception e) { Log.Error("WndProc", e); } // never let an exception unwind into user32
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static void ShowMenu(DeviceCategory category, int x, int y)
    {
        var devices = Registry.Snapshot().Where(d => d.Info.Category == category).ToList();
        nint menu = CreatePopupMenu(), style = CreatePopupMenu();
        try
        {
            foreach (var d in devices)
                AppendMenu(menu, MF_STRING | MF_GRAYED, 0, $"{d.Info.Name}  —  {TrayManager.Describe(d)}");
            if (devices.Count > 0) AppendMenu(menu, MF_SEPARATOR, 0, null);

            AppendMenu(style, MF_STRING | (_settings.IconStyle == IconStyle.Percentage ? MF_CHECKED : 0), CmdStylePercentage, "Porcentagem");
            AppendMenu(style, MF_STRING | (_settings.IconStyle == IconStyle.Glyph ? MF_CHECKED : 0), CmdStyleGlyph, "Símbolo da categoria");
            AppendMenu(menu, MF_POPUP, style, "Estilo do ícone");
            if (devices.Count > 0) AppendMenu(menu, MF_STRING, CmdHideCategory, $"Ocultar ícone de {TrayManager.Label(category)}");
            if (_settings.HiddenCategories.Count > 0) AppendMenu(menu, MF_STRING, CmdShowAll, "Mostrar ícones ocultos");
            AppendMenu(menu, MF_STRING, CmdRefresh, "Atualizar agora");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, "Sair do Voltray");

            SetForegroundWindow(_hwnd); // otherwise the menu does not close when clicking elsewhere
            int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, x, y, 0, _hwnd, 0);
            switch (cmd)
            {
                case CmdRefresh: _ = Task.Run(RefreshAsync); break;
                case CmdExit: PostQuitMessage(0); break;
                case CmdStylePercentage: ChangeSettings(s => s.IconStyle = IconStyle.Percentage); break;
                case CmdStyleGlyph: ChangeSettings(s => s.IconStyle = IconStyle.Glyph); break;
                case CmdHideCategory: ChangeSettings(s => s.HiddenCategories.Add(category)); break;
                case CmdShowAll: ChangeSettings(s => s.HiddenCategories.Clear()); break;
            }
        }
        finally { DestroyMenu(menu); } // destroys the submenu too
    }

    static void ChangeSettings(Action<Settings> change)
    {
        change(_settings);
        try { _settings.Save(); } catch (IOException e) { Log.Error("saving settings", e); }
        _tray?.Update(Registry.Snapshot(), _settings);
    }
}
