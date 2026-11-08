using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Voltray.Core;
using Voltray.Ipc;

namespace Voltray.Settings.ViewModels;

/// One device card. Updated in place when new state arrives, so toggles keep focus.
public sealed partial class DeviceViewModel : INotifyPropertyChanged
{
    DeviceDto _dto;
    bool _shownInTray;

    public DeviceViewModel(DeviceDto dto)
    {
        _dto = dto;
        _shownInTray = dto.ShownInTray;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// Raised when the user flips the tray toggle (not when state arrives from the host).
    public event Action<DeviceViewModel, bool>? ShownInTrayChanged;

    public string Id => _dto.Id;
    public DeviceCategory Category => _dto.Category;
    public bool IsCategoryAutomatic => _dto.CategoryConfidence != Confidence.User;
    public bool Notify => _dto.Notify;
    public string Name => _dto.Name;
    public string Glyph => Categories.Glyph(_dto.Category);

    /// "63%", or "≈44%" when the level is interpolated for a device that reports in coarse steps.
    public string PercentText => _dto is { Interpolated: true, EstimatedPercent: int est } ? $"≈{est}%" : _dto.Percent is int p ? $"{p}%" : _dto.Level switch
    {
        BatteryLevel.Full => "Cheia",
        BatteryLevel.Good => "Boa",
        BatteryLevel.Low => "Baixa",
        BatteryLevel.Critical => "Crítica",
        _ => "—",
    };

    public double PercentValue => (_dto.Interpolated ? _dto.EstimatedPercent : _dto.Percent) ?? 0;
    public bool HasPercent => _dto.Percent != null;
    public Visibility ChargingVisibility => _dto.Connected && _dto.Charge == ChargeState.Charging ? Visibility.Visible : Visibility.Collapsed;
    public double CardOpacity => _dto.Connected ? 1 : 0.55;

    public Brush LevelBrush => (Brush)Application.Current.Resources[
        !_dto.Connected ? "TextFillColorDisabledBrush"
        : _dto.Charge == ChargeState.Charging ? "SystemFillColorSuccessBrush"
        : _dto.Percent is <= 10 ? "SystemFillColorCriticalBrush"
        : _dto.Percent is <= 20 ? "SystemFillColorCautionBrush"
        : "AccentFillColorDefaultBrush"];

    // ---- compact list ----

    public bool IsConnected => _dto.Connected;
    public bool IsCharging => _dto.Connected && _dto.Charge == ChargeState.Charging;
    int? Level => _dto.Interpolated ? _dto.EstimatedPercent : _dto.Percent;

    /// How full the card background is (0..1); devices that only report a level word get a rough fill.
    public double Fraction => (Level ?? _dto.Level switch
    {
        BatteryLevel.Full => 100,
        BatteryLevel.Good => 60,
        BatteryLevel.Low => 20,
        BatteryLevel.Critical => 8,
        _ => 0,
    }) / 100.0;

    /// 0 green, 1 yellow, 2 orange, 3 red — same steps as the tray icon.
    public int Step => (int)Math.Round(Fraction * 100) switch { > 50 => 0, > 20 => 1, > 10 => 2, _ => 3 };

    /// The big number ("63"), or a word when the device reports no percentage.
    public string NumberText => Level is int p ? p.ToString() : PercentText;
    public bool HasNumber => Level is int;

    /// "~14 h restantes", "Carregando", "Desconectado · visto há 2 dias".
    public string CompactDetail =>
        !_dto.Connected ? "Desconectado" + (_dto.UpdatedAt is { } t ? " · visto " + Ago(t) : "")
        : IsCharging ? "Carregando"
        : _dto.RemainingMinutes is int min ? Remaining(min) + " restantes"
        : _dto.UpdatedAt is { } u ? "Atualizado " + Ago(u)
        : _dto.Manufacturer ?? "";

    public DateTimeOffset? UpdatedAt => _dto.UpdatedAt;

    /// "Logitech · HID++ 0x1004 · há 2 min" or "Desconectado · visto há 3 h".
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!_dto.Connected) parts.Add("Desconectado");
            else if (_dto.RemainingMinutes is int min && _dto.Charge != ChargeState.Charging) parts.Add(Remaining(min) + " restantes");
            if (_dto.Manufacturer is { } m) parts.Add(m);
            if (_dto.Source is { } s && _dto.Connected) parts.Add(s);
            if (_dto.UpdatedAt is { } t) parts.Add((_dto.Connected ? "atualizado " : "visto ") + Ago(t));
            return string.Join(" · ", parts);
        }
    }

    /// Same wording as the tray tooltip: "~45 min", "~14 h", "~3 dias".
    static string Remaining(int minutes) =>
        minutes < 60 ? $"~{Math.Max(1, minutes)} min" : minutes < 48 * 60 ? $"~{(int)Math.Round(minutes / 60.0)} h" : $"~{(int)Math.Round(minutes / 1440.0)} dias";

    public string BatteryAutomationName => $"{Name}: bateria {PercentText}{(_dto.Charge == ChargeState.Charging ? ", carregando" : "")}";

    public bool ShownInTray
    {
        get => _shownInTray;
        set
        {
            if (_shownInTray == value) return;
            _shownInTray = value;
            OnChanged();
            ShownInTrayChanged?.Invoke(this, value);
        }
    }

    public void Update(DeviceDto dto)
    {
        _dto = dto;
        if (_shownInTray != dto.ShownInTray) { _shownInTray = dto.ShownInTray; OnChanged(nameof(ShownInTray)); }
        RefreshText();
    }

    /// Re-raises every computed property (also used every minute so "há 2 min" stays current).
    public void RefreshText()
    {
        foreach (var p in new[] { nameof(Name), nameof(Glyph), nameof(PercentText), nameof(PercentValue), nameof(HasPercent),
                     nameof(ChargingVisibility), nameof(CardOpacity), nameof(LevelBrush), nameof(Detail), nameof(BatteryAutomationName) })
            OnChanged(p);
    }

    public static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        return d.TotalMinutes < 1 ? "agora" : d.TotalHours < 1 ? $"há {(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"há {(int)d.TotalHours} h" : $"em {t:dd/MM}";
    }

    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
