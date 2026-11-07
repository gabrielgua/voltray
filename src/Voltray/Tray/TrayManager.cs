using Microsoft.Win32;
using Voltray.Core;
using static Voltray.Interop.Win32;

namespace Voltray.Tray;

/// One notification-area icon per device category that currently has devices.
/// Each category has a fixed GUID, so Windows remembers per category whether the user
/// pinned it to the taskbar or left it in the overflow area. We never force that choice.
internal sealed unsafe class TrayManager(nint hwnd, uint callbackMessage)
{
    static readonly Dictionary<DeviceCategory, Guid> IconGuids = new()
    {
        [DeviceCategory.Mouse] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f50"),
        [DeviceCategory.Keyboard] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f51"),
        [DeviceCategory.Headphones] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f52"),
        [DeviceCategory.Earbuds] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f53"),
        [DeviceCategory.Controller] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f54"),
        [DeviceCategory.Other] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f55"),
    };

    readonly Dictionary<DeviceCategory, nint> _shown = new(); // category -> current HICON

    public static string Label(DeviceCategory c) => c switch
    {
        DeviceCategory.Mouse => "Mouse",
        DeviceCategory.Keyboard => "Teclado",
        DeviceCategory.Headphones => "Headphones",
        DeviceCategory.Earbuds => "Earbuds",
        DeviceCategory.Controller => "Controle",
        _ => "Outros",
    };

    public static string Describe(DeviceState d) => d switch
    {
        { Connected: false, Battery.Percent: int p } => $"desconectado (último: {p}%)",
        { Connected: false } => "desconectado",
        { Battery: null } => "sem dados",
        { Battery: { Percent: int p, Charge: ChargeState.Charging } } => $"{p}% carregando",
        { Battery.Percent: int p } => $"{p}%",
        _ => d.Battery!.Level.ToString().ToLowerInvariant(),
    };

    /// Each category gets its own callback message (callbackMessage + category): with NIF_GUID
    /// the shell does not reliably report uID, so the message itself identifies the icon.
    public bool TryGetCategory(uint msg, out DeviceCategory category)
    {
        category = (DeviceCategory)(msg - callbackMessage);
        return msg >= callbackMessage && msg - callbackMessage < (uint)IconGuids.Count;
    }

    public void Update(IReadOnlyList<DeviceState> devices, Settings settings)
    {
        var groups = devices.GroupBy(d => d.Info.Category)
            .Where(g => !settings.HiddenCategories.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Nothing to show (still scanning, no devices, or all hidden): keep one icon so the app stays reachable.
        if (groups.Count == 0) groups[DeviceCategory.Other] = [];

        foreach (var gone in _shown.Keys.Where(c => !groups.ContainsKey(c)).ToList()) Remove(gone);
        foreach (var (category, list) in groups) Show(category, list, settings.IconStyle);
    }

    /// After Explorer restarts every icon must be added again.
    public void Recreate(IReadOnlyList<DeviceState> devices, Settings settings)
    {
        foreach (var c in _shown.Keys.ToList()) { DestroyIcon(_shown[c]); _shown.Remove(c); }
        Update(devices, settings);
    }

    void Show(DeviceCategory category, List<DeviceState> list, IconStyle style)
    {
        // The icon shows the device that needs attention: the lowest connected battery.
        var lowest = list.Where(d => d.Connected && d.Battery?.Percent != null).MinBy(d => d.Battery!.Percent);
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        var px = IconRenderer.Render(style, category, lowest?.Battery?.Percent, lowest?.Battery?.Charge == ChargeState.Charging, LightTaskbar(), size);
        nint icon = IconRenderer.CreateHIcon(px, size);

        var nid = Data(category);
        nid.uFlags |= NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        nid.uCallbackMessage = callbackMessage + (uint)category;
        nid.hIcon = icon;
        string tip = list.Count == 0
            ? "Voltray\nNenhum dispositivo com bateria encontrado"
            : Label(category) + "\n" + string.Join("\n", list.Select(d => $"{Shorten(d.Info.Name, 24)} — {Describe(d)}"));
        if (tip.Length > 127) tip = tip[..126] + "…"; // szTip limit is 128 chars including the terminator
        tip.AsSpan().CopyTo(new Span<char>(nid.szTip, 128));

        if (_shown.TryGetValue(category, out var old))
        {
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
            DestroyIcon(old); // the shell keeps its own copy
        }
        else
        {
            var del = Data(category);
            Shell_NotifyIcon(NIM_DELETE, ref del); // a stale entry from a crashed run makes NIM_ADD fail
            if (!Shell_NotifyIcon(NIM_ADD, ref nid)) Log.Error($"NIM_ADD failed for {category} icon");
            else Log.Info($"tray icon added: {category}");
            nid.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref nid);
        }
        _shown[category] = icon;
    }

    void Remove(DeviceCategory category)
    {
        var nid = Data(category);
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        DestroyIcon(_shown[category]);
        _shown.Remove(category);
    }

    public void RemoveAll()
    {
        foreach (var c in _shown.Keys.ToList()) Remove(c);
    }

    NOTIFYICONDATAW Data(DeviceCategory c) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = hwnd, uID = (uint)c, uFlags = NIF_GUID, guidItem = IconGuids[c],
    };

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static bool LightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
}
