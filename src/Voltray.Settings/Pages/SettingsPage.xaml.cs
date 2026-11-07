using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Settings.ViewModels;

namespace Voltray.Settings.Pages;

public sealed partial class SettingsPage : UserControl
{
    static readonly int[] Intervals = [1, 5, 15, 30];
    static readonly int[] Thresholds = [10, 15, 20, 25, 30, 40];

    readonly Dictionary<DeviceCategory, CheckBox> _categoryChecks = new();
    bool _applying; // true while copying host state into the controls, so their events don't echo it back

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var c in Categories.All)
        {
            var box = new CheckBox { Content = Categories.Label(c), Tag = c };
            box.Click += OnCategoryClick;
            _categoryChecks[c] = box;
            CategoryChecks.Children.Add(box);
        }
        foreach (var m in Intervals) IntervalBox.Items.Add(new ComboBoxItem { Content = m == 1 ? "1 minuto" : $"{m} minutos", Tag = m });
        foreach (var t in Thresholds) ThresholdBox.Items.Add(new ComboBoxItem { Content = $"{t}%", Tag = t });

        IsEnabled = App.Host.State != null;
        App.Host.StateReceived += Apply;
        if (App.Host.State is { } s) Apply(s);
        Unloaded += (_, _) => App.Host.StateReceived -= Apply;
    }

    void Apply(StateDto state)
    {
        _applying = true;
        try
        {
            IsEnabled = true;
            var s = state.Settings;
            IconStyleBox.SelectedIndex = s.IconStyle == IconStyle.Percentage ? 0 : 1;
            foreach (var (c, box) in _categoryChecks) box.IsChecked = !s.HiddenCategories.Contains(c);
            AutostartSwitch.IsOn = s.StartWithWindows;
            int i = Array.IndexOf(Intervals, s.RefreshMinutes);
            IntervalBox.SelectedIndex = i >= 0 ? i : 1;
            NotifyLowSwitch.IsOn = s.NotifyLowBattery;
            NotifyChargedSwitch.IsOn = s.NotifyCharged;
            int t = Array.IndexOf(Thresholds, s.LowBatteryThreshold);
            ThresholdBox.SelectedIndex = t >= 0 ? t : Array.IndexOf(Thresholds, 20);
            ThresholdBox.IsEnabled = s.NotifyLowBattery;
        }
        finally { _applying = false; }
    }

    void OnIconStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || IconStyleBox.SelectedIndex < 0) return;
        var style = IconStyleBox.SelectedIndex == 0 ? IconStyle.Percentage : IconStyle.Glyph;
        _ = App.Host.ChangeSettingsAsync(s => s.IconStyle = style);
    }

    void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (_applying || sender is not CheckBox { Tag: DeviceCategory c } box) return;
        bool shown = box.IsChecked == true;
        _ = App.Host.ChangeSettingsAsync(s => { if (shown) s.HiddenCategories.Remove(c); else s.HiddenCategories.Add(c); });
    }

    void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = AutostartSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.StartWithWindows = on);
    }

    void OnNotifyLowToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = NotifyLowSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.NotifyLowBattery = on);
    }

    void OnNotifyChargedToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = NotifyChargedSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.NotifyCharged = on);
    }

    void OnThresholdChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || ThresholdBox.SelectedItem is not ComboBoxItem { Tag: int percent }) return;
        _ = App.Host.ChangeSettingsAsync(s => s.LowBatteryThreshold = percent);
    }

    void OnTestNotificationClick(object sender, RoutedEventArgs e) => _ = App.Host.TestNotificationAsync();

    void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || IntervalBox.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;
        _ = App.Host.ChangeSettingsAsync(s => s.RefreshMinutes = minutes);
    }
}
