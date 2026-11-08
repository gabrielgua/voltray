using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Voltray.Ipc;
using Voltray.Settings.Controls;
using Voltray.Settings.ViewModels;

namespace Voltray.Settings.Pages;

/// The compact window's main view: one row per device, connected first.
public sealed partial class DeviceListView : Grid
{
    readonly StackPanel _list = new() { Spacing = 4, Padding = new Thickness(8, 0, 8, 8) };
    readonly TextBlock _count = new(), _updated = new();
    readonly TextBlock _empty = new()
    {
        Text = "Nenhum dispositivo com bateria.\nConecte um mouse, teclado ou fone sem fio; dispositivos Bluetooth precisam estar pareados.",
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(24, 32, 24, 0),
    };
    readonly Dictionary<string, DeviceRow> _rows = [];
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };

    /// Raised when the number of rows changes, so the window can fit its height.
    public event Action<int>? CountChanged;
    public event Action? SettingsRequested;

    public DeviceListView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: title, refresh, settings
        var header = new Grid { Padding = new Thickness(16, 4, 8, 8), ColumnSpacing = 2 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "Dispositivos", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        var refresh = IconButton("", "Atualizar agora");
        refresh.Click += (_, _) => _ = App.Host.RefreshAsync();
        var settings = IconButton("", "Configurações");
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        Grid.SetColumn(refresh, 1); Grid.SetColumn(settings, 2);
        header.Children.Add(refresh);
        header.Children.Add(settings);
        Children.Add(header);

        var scroll = new ScrollViewer { Content = new StackPanel { Children = { _list, _empty } } };
        Grid.SetRow(scroll, 1);
        Children.Add(scroll);

        // Footer: count and last update
        var secondary = (Style)Application.Current.Resources["SecondaryTextStyle"];
        _count.Style = _updated.Style = secondary;
        var footer = new Grid { Padding = new Thickness(16, 8, 16, 10), BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] };
        footer.Children.Add(_count);
        _updated.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(_updated);
        Grid.SetRow(footer, 2);
        Children.Add(footer);

        if (App.Demo) { Loaded += (_, _) => Apply(DemoState()); return; }
        _clock.Tick += (_, _) => { foreach (var r in _rows.Values) { r.Device.RefreshText(); r.Refresh(); } UpdateFooter(); };
        // The window swaps this view out for Settings and back, so (re)subscribe on every load:
        // settings changed meanwhile (e.g. the charging animation) show up as soon as it returns.
        Loaded += (_, _) =>
        {
            App.Host.StateReceived -= Apply;
            App.Host.StateReceived += Apply;
            if (App.Host.State is { } s) Apply(s);
            _clock.Start();
        };
        Unloaded += (_, _) => { App.Host.StateReceived -= Apply; _clock.Stop(); };
    }

    static Button IconButton(string glyph, string name)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 16 }, Width = 36, Height = 32, Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(b, name);
        ToolTipService.SetToolTip(b, name);
        return b;
    }

    void Apply(StateDto state)
    {
        DeviceRow.Animation = state.Settings.ChargingAnimation;
        var ordered = state.Devices
            .OrderByDescending(d => d.Connected)
            .ThenBy(d => Categories.All.ToList().IndexOf(d.Category))
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var gone in _rows.Keys.Where(id => ordered.All(d => d.Id != id)).ToList())
        {
            _list.Children.Remove(_rows[gone]);
            _rows.Remove(gone);
        }
        for (int i = 0; i < ordered.Count; i++)
        {
            var dto = ordered[i];
            if (!_rows.TryGetValue(dto.Id, out var row))
            {
                var vm = new DeviceViewModel(dto);
                vm.ShownInTrayChanged += (v, shown) =>
                    _ = App.Host.ChangeSettingsAsync(s => { if (shown) s.HiddenDevices.Remove(v.Id); else s.HiddenDevices.Add(v.Id); });
                row = new DeviceRow(vm);
                _rows[dto.Id] = row;
                _list.Children.Insert(i, row);
            }
            else
            {
                row.Device.Update(dto);
                row.Refresh();
                int at = _list.Children.IndexOf(row);
                if (at != i) { _list.Children.RemoveAt(at); _list.Children.Insert(i, row); }
            }
        }
        _empty.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFooter();
        CountChanged?.Invoke(ordered.Count);
    }

    /// --demo: sample devices (one charging) for screenshots and for trying the charging animations.
    static StateDto DemoState()
    {
        var now = DateTimeOffset.Now;
        var settings = new Ipc.Settings();
        if (Environment.GetCommandLineArgs() is var a && Array.IndexOf(a, "--demo") is int i && i + 1 < a.Length && Enum.TryParse<ChargingAnimation>(a[i + 1], true, out var anim))
            settings.ChargingAnimation = anim;
        DeviceDto D(string id, string name, Core.DeviceCategory c, int pct, bool charging = false, bool connected = true, int? remaining = null) =>
            new(id, name, null, c, Core.Confidence.Reported, connected, pct, Core.BatteryLevel.Good,
                charging ? Core.ChargeState.Charging : Core.ChargeState.Discharging, "Demo", connected ? now : now.AddDays(-2), true, true, RemainingMinutes: remaining);
        return new StateDto("demo", [
            D("1", "PRO X Wireless", Core.DeviceCategory.Headphones, 63, remaining: 14 * 60),
            D("2", "MX Master 3S", Core.DeviceCategory.Mouse, 44, remaining: 3 * 1440),
            D("3", "Xbox Wireless Controller", Core.DeviceCategory.Controller, 46, charging: true),
            D("4", "MX Keys", Core.DeviceCategory.Keyboard, 18, remaining: 2 * 1440),
            D("5", "Galaxy Buds2", Core.DeviceCategory.Earbuds, 9, remaining: 25),
            D("6", "JBL Flip 6", Core.DeviceCategory.Other, 70, connected: false),
        ], settings);
    }

    void UpdateFooter()
    {
        int n = _rows.Count;
        _count.Text = n == 1 ? "1 dispositivo" : $"{n} dispositivos";
        var last = _rows.Values.Select(r => r.Device.UpdatedAt).Where(t => t != null).Max();
        _updated.Text = last is { } t ? "Atualizado " + DeviceViewModel.Ago(t) : "";
    }
}
