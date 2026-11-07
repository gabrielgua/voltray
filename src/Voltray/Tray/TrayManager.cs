using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Rendering;
using static Voltray.Interop.Win32;

namespace Voltray.Tray;

/// What a tray icon stands for: a whole category, or one device (per-device mode).
internal readonly record struct IconTarget(DeviceCategory Category, string? DeviceId);

/// Notification-area icons, one per category or one per device (Settings.TrayMode).
/// Every icon has a stable GUID — fixed per category, derived from the device id per device — so
/// Windows remembers for each one whether the user pinned it next to the clock. We never force that.
internal sealed unsafe class TrayManager(nint hwnd, uint callbackMessage)
{
    /// Callback messages callbackMessage .. callbackMessage + MaxSlots - 1 identify the icon clicked
    /// (with NIF_GUID the shell does not reliably report uID).
    public const int MaxSlots = 64;

    static readonly Dictionary<DeviceCategory, Guid> CategoryGuids = new()
    {
        [DeviceCategory.Mouse] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f50"),
        [DeviceCategory.Keyboard] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f51"),
        [DeviceCategory.Headphones] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f52"),
        [DeviceCategory.Earbuds] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f53"),
        [DeviceCategory.Controller] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f54"),
        [DeviceCategory.Other] = new("6f1e4b52-3c1a-4d8e-9b57-0a1c2d3e4f55"),
    };

    sealed class Slot(int index, IconTarget target, Guid guid)
    {
        public int Index { get; } = index;
        public IconTarget Target { get; } = target;
        public Guid Guid { get; } = guid;
        public nint Icon;
    }

    readonly Dictionary<IconTarget, Slot> _shown = new();

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

    public bool TryGetTarget(uint msg, out IconTarget target)
    {
        target = default;
        if (msg < callbackMessage || msg >= callbackMessage + MaxSlots) return false;
        int index = (int)(msg - callbackMessage);
        var slot = _shown.Values.FirstOrDefault(s => s.Index == index);
        if (slot is null) return false;
        target = slot.Target;
        return true;
    }

    public void Update(IReadOnlyList<DeviceState> devices, Settings settings)
    {
        var visible = devices
            .Where(d => !settings.HiddenDevices.Contains(d.Info.Id) && !settings.HiddenCategories.Contains(d.Info.Category))
            .Where(d => d.Connected || settings.Disconnected == DisconnectedInTray.Dim)
            .ToList();
        var options = new IconOptions(settings.ShowPercentSign, settings.ColorNumber);

        var wanted = new Dictionary<IconTarget, List<DeviceState>>();
        if (settings.TrayMode == TrayMode.PerDevice)
            foreach (var d in visible) wanted[new IconTarget(d.Info.Category, d.Info.Id)] = [d];
        else
            foreach (var g in visible.GroupBy(d => d.Info.Category)) wanted[new IconTarget(g.Key, null)] = [.. g];

        // Nothing to show (still scanning, no devices, or all hidden): keep one icon so the app stays reachable.
        if (wanted.Count == 0) wanted[new IconTarget(DeviceCategory.Other, null)] = [];

        foreach (var gone in _shown.Keys.Where(t => !wanted.ContainsKey(t)).ToList()) Remove(gone);
        foreach (var (target, list) in wanted)
        {
            string? iconKey = target.DeviceId is { } id ? settings.DeviceIcons.GetValueOrDefault(id) : null;
            Show(target, list, settings.IconStyle, options with { IconKey = iconKey });
        }
    }

    /// After Explorer restarts every icon must be added again.
    public void Recreate(IReadOnlyList<DeviceState> devices, Settings settings)
    {
        foreach (var s in _shown.Values) DestroyIcon(s.Icon);
        _shown.Clear();
        Update(devices, settings);
    }

    void Show(IconTarget target, List<DeviceState> list, IconStyle style, IconOptions options)
    {
        // The icon shows the device that needs attention: the lowest connected battery. When every
        // device is disconnected (Dim rule), it shows the last known level, faded.
        var connected = list.Where(d => d.Connected && d.Battery?.Percent != null).ToList();
        var shown = (connected.Count > 0 ? connected : list.Where(d => d.Battery?.Percent != null)).MinBy(d => d.Battery!.Percent);
        bool dim = list.Count > 0 && connected.Count == 0 && list.All(d => !d.Connected);

        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        var px = IconRenderer.Render(style, target.Category, shown?.Battery?.Percent, shown?.Battery?.Charge == ChargeState.Charging,
            LightTaskbar(), size, options with { Dimmed = dim });
        nint icon = IconRenderer.CreateHIcon(px, size);

        if (!_shown.TryGetValue(target, out var slot))
        {
            int index = Enumerable.Range(0, MaxSlots).FirstOrDefault(i => _shown.Values.All(s => s.Index != i), -1);
            if (index < 0) { DestroyIcon(icon); Log.Error("too many tray icons"); return; }
            slot = new Slot(index, target, GuidFor(target));
        }

        var nid = Data(slot);
        nid.uFlags |= NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        nid.uCallbackMessage = callbackMessage + (uint)slot.Index;
        nid.hIcon = icon;
        string tip = list.Count == 0 ? "Voltray\nNenhum dispositivo com bateria encontrado"
            : target.DeviceId != null ? $"{Shorten(list[0].Info.Name, 60)}\n{Describe(list[0])}"
            : Label(target.Category) + "\n" + string.Join("\n", list.Select(d => $"{Shorten(d.Info.Name, 24)} — {Describe(d)}"));
        if (tip.Length > 127) tip = tip[..126] + "…"; // szTip limit is 128 chars including the terminator
        tip.AsSpan().CopyTo(new Span<char>(nid.szTip, 128));

        if (_shown.ContainsKey(target))
        {
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
            DestroyIcon(slot.Icon); // the shell keeps its own copy
        }
        else
        {
            var del = Data(slot);
            Shell_NotifyIcon(NIM_DELETE, ref del); // a stale entry from a crashed run makes NIM_ADD fail
            if (!Shell_NotifyIcon(NIM_ADD, ref nid)) Log.Error($"NIM_ADD failed for {Name(target)}");
            else Log.Info($"tray icon added: {Name(target)}");
            nid.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref nid);
            _shown[target] = slot;
        }
        slot.Icon = icon;
    }

    void Remove(IconTarget target)
    {
        var slot = _shown[target];
        var nid = Data(slot);
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        DestroyIcon(slot.Icon);
        _shown.Remove(target);
    }

    /// Shows a notification from the device's icon, else its category's, else any shown icon.
    /// Windows 10/11 turn this into a regular toast; NIIF_RESPECT_QUIET_TIME lets Do Not Disturb
    /// (including the automatic one during full-screen games) hold it back.
    public bool Notify(DeviceCategory category, string title, string text, bool warning, string? deviceId = null)
    {
        if (_shown.Count == 0) return false;
        var slot = (deviceId != null ? _shown.GetValueOrDefault(new IconTarget(category, deviceId)) : null)
                   ?? _shown.GetValueOrDefault(new IconTarget(category, null))
                   ?? _shown.Values.First();
        var nid = Data(slot);
        nid.uFlags |= NIF_INFO;
        nid.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_RESPECT_QUIET_TIME;
        title.AsSpan(0, Math.Min(title.Length, 63)).CopyTo(new Span<char>(nid.szInfoTitle, 64));
        text.AsSpan(0, Math.Min(text.Length, 255)).CopyTo(new Span<char>(nid.szInfo, 256));
        bool ok = Shell_NotifyIcon(NIM_MODIFY, ref nid);
        Log.Info($"notification {(ok ? "shown" : "FAILED")}: {title} — {text}");
        return ok;
    }

    public void RemoveAll()
    {
        foreach (var t in _shown.Keys.ToList()) Remove(t);
    }

    NOTIFYICONDATAW Data(Slot s) => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = hwnd, uID = (uint)s.Index, uFlags = NIF_GUID, guidItem = s.Guid,
    };

    /// Category icons keep their historical GUIDs; device icons get a name-based GUID from the device id.
    static Guid GuidFor(IconTarget t)
    {
        if (t.DeviceId is null) return CategoryGuids[t.Category];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("voltray-tray:" + t.DeviceId));
        hash[6] = (byte)(hash[6] & 0x0F | 0x30); // version 3 (name-based, MD5)
        hash[8] = (byte)(hash[8] & 0x3F | 0x80); // RFC 4122 variant
        return new Guid(hash);
    }

    static string Name(IconTarget t) => t.DeviceId ?? t.Category.ToString();

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static bool LightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
}
