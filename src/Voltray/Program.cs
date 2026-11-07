using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Providers.Bluetooth;
using Voltray.Providers.Logitech;
using Voltray.Tray;
using static Voltray.Interop.Win32;

namespace Voltray;

/// Background host: owns the providers, the device registry, the tray icons and the IPC server.
/// Pure Win32 + NativeAOT so it idles at a few MB; the settings window is a separate process.
internal static class Program
{
    const uint WM_DEVICES_CHANGED = WM_APP + 1, WM_INVOKE = WM_APP + 2, WM_TRAY_BASE = WM_APP + 0x10;
    const nint TimerRefresh = 1;
    const int CmdRefresh = 1, CmdExit = 2, CmdStylePercentage = 3, CmdStyleGlyph = 4, CmdHideCategory = 5, CmdShowAll = 6, CmdOpen = 7;

    /// Window class + title of the settings window (WinUI 3 desktop windows use this class).
    const string SettingsWindowClass = "WinUIDesktopWin32WindowClass", SettingsWindowTitle = "Voltray";

    static nint _hwnd;
    static uint _taskbarCreated;
    static int _updatePending;
    static Settings _settings = new();
    static readonly DeviceRegistry Registry = new();
    static readonly List<IBatteryProvider> Providers = [new LogitechProvider(), new BluetoothProvider()];
    static readonly ConcurrentQueue<Action> UiQueue = new();
    static readonly BatteryAlerts Alerts = new();
    static TrayManager? _tray;
    static IpcServer? _ipc;
    static volatile StateDto _state = new("", [], new());

    [STAThread]
    static unsafe int Main(string[] args)
    {
        if (args is ["--icon-preview", var path]) { IconRenderer.WritePreview(path); return 0; }

        using var single = new Mutex(true, @"Local\Voltray", out bool first);
        if (!first)
        {
            if (!args.Contains("--background")) OpenSettingsWindow(); // second launch from Start menu = "show me Voltray"
            return 0;
        }

        Log.Info($"Voltray {Version} starting");
        _settings = Settings.Load();
        _settings.StartWithWindows = Autostart.IsEnabled();
        Autostart.Repair();
        foreach (var (id, category) in _settings.CategoryOverrides) Registry.OverrideCategory(id, category);

        fixed (char* cls = "VoltrayTray")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, lpszClassName = cls };
            RegisterClassEx(ref wc);
        }
        _hwnd = CreateWindowEx(0, "VoltrayTray", "Voltray tray", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _tray = new TrayManager(_hwnd, WM_TRAY_BASE); // icons appear as devices are found

        _state = BuildState();
        _ipc = new IpcServer(() => _state, m => Invoke(() => HandleCommand(m)));
        _ipc.Start();

        // Providers report from background threads; coalesce into one repaint on the UI thread.
        Registry.Changed += () => { if (Interlocked.Exchange(ref _updatePending, 1) == 0) PostMessage(_hwnd, WM_DEVICES_CHANGED, 0, 0); };
        _ = Task.Run(StartProvidersAsync);
        SetTimer(_hwnd, TimerRefresh, (uint)RefreshInterval().TotalMilliseconds, 0);

        while (GetMessage(out var msg, 0, 0, 0) > 0) { TranslateMessage(msg); DispatchMessage(msg); }

        _tray.RemoveAll();
        foreach (var p in Providers) p.DisposeAsync().AsTask().Wait(1000);
        return 0;
    }

    static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// Runs <paramref name="action"/> on the UI thread (settings and tray icons are only touched there).
    static void Invoke(Action action)
    {
        UiQueue.Enqueue(action);
        PostMessage(_hwnd, WM_INVOKE, 0, 0);
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

    static StateDto BuildState()
    {
        var devices = Registry.Snapshot()
            .OrderBy(d => d.Info.Category).ThenBy(d => d.Info.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new DeviceDto(d.Info.Id, d.Info.Name, d.Info.Manufacturer, d.Info.Category, d.Info.CategoryConfidence, d.Connected,
                d.Battery?.Percent, d.Battery?.Level ?? BatteryLevel.Unknown, d.Battery?.Charge ?? ChargeState.Unknown,
                d.Battery?.Source, d.Battery?.Timestamp, !_settings.HiddenDevices.Contains(d.Info.Id), !_settings.MutedDevices.Contains(d.Info.Id)))
            .ToList();
        return new StateDto(Version, devices, _settings.Clone());
    }

    /// Repaints the tray and pushes the new state to open settings windows. UI thread only.
    static void StateChanged()
    {
        var devices = Registry.Snapshot();
        _tray?.Update(devices, _settings);
        _state = BuildState();
        _ipc?.Broadcast(_state);
        NotifyAlerts(devices);
    }

    static void NotifyAlerts(IReadOnlyList<DeviceState> devices)
    {
        var options = new AlertOptions(_settings.NotifyLowBattery, _settings.LowBatteryThreshold, _settings.NotifyCharged, _settings.MutedDevices);
        foreach (var a in Alerts.Evaluate(devices, options))
        {
            var (title, text) = a.Kind switch
            {
                AlertKind.Critical => ($"Bateria crítica: {a.DeviceName}", $"{a.Percent}% restantes. Conecte o carregador logo."),
                AlertKind.Low => ($"Bateria baixa: {a.DeviceName}", $"{a.Percent}% restantes."),
                _ => ($"Carga completa: {a.DeviceName}", "Pode desconectar o carregador."),
            };
            _tray?.Notify(a.Category, title, text, warning: a.Kind != AlertKind.Charged);
        }
    }

    static void HandleCommand(Message m)
    {
        switch (m.Type)
        {
            case MessageType.SetSettings when m.Settings is { } s:
                if (s.StartWithWindows != _settings.StartWithWindows) Autostart.Set(s.StartWithWindows);
                bool intervalChanged = s.RefreshMinutes != _settings.RefreshMinutes;
                _settings = s;
                SaveSettings();
                if (intervalChanged) SetTimer(_hwnd, TimerRefresh, (uint)RefreshInterval().TotalMilliseconds, 0);
                StateChanged();
                break;
            case MessageType.SetCategory when m.DeviceId is { } id:
                if (m.Category is DeviceCategory c) _settings.CategoryOverrides[id] = c;
                else _settings.CategoryOverrides.Remove(id);
                SaveSettings();
                Registry.OverrideCategory(id, m.Category); // raises Changed → StateChanged
                break;
            case MessageType.Refresh:
                _ = Task.Run(RefreshAsync);
                break;
            case MessageType.TestNotification:
                _tray?.Notify(DeviceCategory.Mouse, "Bateria baixa: dispositivo de teste",
                    $"{_settings.LowBatteryThreshold}% restantes. É assim que os avisos do Voltray aparecem.", warning: true);
                break;
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
                if (ev is NIN_SELECT or NIN_KEYSELECT or NIN_BALLOONUSERCLICK) OpenSettingsWindow(); // left click / Enter / notification click
                else if (ev == WM_CONTEXTMENU) ShowMenu(category, (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return 0;
            }
            switch (msg)
            {
                case WM_DEVICES_CHANGED:
                    Interlocked.Exchange(ref _updatePending, 0);
                    StateChanged();
                    return 0;
                case WM_INVOKE:
                    while (UiQueue.TryDequeue(out var action)) action();
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
        var devices = Registry.Snapshot().Where(d => d.Info.Category == category && !_settings.HiddenDevices.Contains(d.Info.Id)).ToList();
        nint menu = CreatePopupMenu(), style = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, CmdOpen, "Abrir Voltray");
            SetMenuDefaultItem(menu, CmdOpen, 0); // bold, like other tray apps
            AppendMenu(menu, MF_SEPARATOR, 0, null);
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
                case CmdOpen: OpenSettingsWindow(); break;
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
        SaveSettings();
        StateChanged();
    }

    static void SaveSettings()
    {
        try { _settings.Save(); } catch (IOException e) { Log.Error("saving settings", e); }
    }

    /// Brings the settings window to the front, or starts it. It is a separate WinUI 3 process
    /// so the ~100 MB it needs are released when it closes.
    static void OpenSettingsWindow()
    {
        nint existing = FindWindow(SettingsWindowClass, SettingsWindowTitle);
        if (existing != 0)
        {
            if (IsIconic(existing)) ShowWindow(existing, SW_RESTORE);
            SetForegroundWindow(existing);
            return;
        }
        string[] candidates =
        [
            Environment.GetEnvironmentVariable("VOLTRAY_SETTINGS_EXE") ?? "",
            Path.Combine(AppContext.BaseDirectory, "settings", "Voltray.Settings.exe"),
            Path.Combine(AppContext.BaseDirectory, "Voltray.Settings.exe"),
        ];
        var exe = candidates.FirstOrDefault(File.Exists);
        if (exe is null) { Log.Error("settings window not found next to Voltray.exe"); return; }
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); }
        catch (Exception e) { Log.Error($"starting {exe}", e); }
    }
}
