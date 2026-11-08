using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Voltray.Core.Icons;
using Voltray.Ipc;
using Voltray.Settings.Pages;
using Voltray.Settings.ViewModels;
using Windows.UI;

namespace Voltray.Settings.Controls;

/// One device in the compact list: icon, name, detail and a big percentage over a background that
/// is the battery level itself. Clicking it opens the device menu.
public sealed partial class DeviceRow : Button
{
    static readonly Color TealDark = Color.FromArgb(255, 0x6F, 0xF0, 0xE0), TealLight = Color.FromArgb(255, 0x00, 0x7A, 0x74);

    readonly LevelFill _fill = new();
    readonly TextLayer _outside, _inside; // _inside: white copy clipped to the fill (charging)
    readonly RectangleGeometry _insideClip = new();
    string? _iconPath;

    public DeviceViewModel Device { get; }

    public DeviceRow(DeviceViewModel vm)
    {
        Device = vm;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(0);
        MinHeight = 58;
        CornerRadius = new CornerRadius(6);
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        _outside = new TextLayer(null);
        _inside = new TextLayer(new SolidColorBrush(Colors.White)) { Root = { Clip = _insideClip } };
        var content = new Grid();
        content.Children.Add(_fill);
        content.Children.Add(_outside.Root);
        content.Children.Add(_inside.Root);
        Content = content;

        _fill.FillChanged += UpdateClip;
        _fill.SizeChanged += (_, _) => UpdateClip();
        ActualThemeChanged += (_, _) => Refresh();
        Click += (_, _) => DeviceMenu.Show(Device, this);
        Refresh();
    }

    public static ChargingAnimation Animation { get; set; } = ChargingAnimation.Stream;

    /// Re-reads the view model (after a state update or a setting change).
    public void Refresh()
    {
        var vm = Device;
        var settings = App.Host.State?.Settings;
        string iconPath = settings?.DeviceIcons.GetValueOrDefault(vm.Id) is { } key && DeviceIcons.Find(key) is { } e ? e.Path : CategoryIcons.For(vm.Category);

        var kind = !vm.IsConnected ? FillKind.Static
            : vm.IsCharging ? Animation switch { ChargingAnimation.Bubbles => FillKind.Bubbles, ChargingAnimation.Sparks => FillKind.Sparks, _ => FillKind.Stream }
            : FillKind.Static;
        _fill.Set(vm.Fraction, kind, vm.Step);

        bool dark = ActualTheme == ElementTheme.Dark;
        Brush? number = vm.IsCharging ? new SolidColorBrush(dark ? TealDark : TealLight) : null;
        Brush? detail = vm.IsCharging && dark ? new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)) : null;
        bool newIcon = iconPath != _iconPath;
        _iconPath = iconPath;
        _outside.Set(vm, newIcon ? iconPath : null, number, detail);
        _inside.Set(vm, newIcon ? iconPath : null, null, null);
        _inside.Root.Visibility = vm.IsCharging ? Visibility.Visible : Visibility.Collapsed; // white over the fill, so a high level never hides the teal number

        _outside.Root.Opacity = _inside.Root.Opacity = vm.IsConnected ? 1 : 0.45;
        _fill.Opacity = vm.IsConnected ? 1 : 0.45;
        AutomationProperties.SetName(this, vm.BatteryAutomationName);
        ToolTipService.SetToolTip(this, vm.Detail);
    }

    void UpdateClip() => _insideClip.Rect = new Windows.Foundation.Rect(0, 0, _fill.FillWidth, Math.Max(0, _fill.ActualHeight));

    /// Icon, name, detail and number. Two of these are stacked so text over the fill can differ in colour.
    sealed class TextLayer
    {
        public Grid Root { get; } = new() { Padding = new Thickness(12, 9, 14, 9), ColumnSpacing = 12, IsHitTestVisible = false };
        readonly ContentPresenter _icon = new() { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _detail = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Run _value = new() { FontSize = 24 }, _unit = new() { FontSize = 14, Text = "%" };
        readonly TextBlock _number;
        readonly Brush? _force;

        public TextLayer(Brush? force)
        {
            _force = force;
            _number = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Display"), FontWeight = FontWeights.SemiBold,
                TextLineBounds = TextLineBounds.Tight, VerticalAlignment = VerticalAlignment.Center,
                Inlines = { _value, _unit },
            };
            foreach (var w in new[] { new GridLength(28), new GridLength(1, GridUnitType.Star), GridLength.Auto })
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _name, _detail } };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(_number, 2);
            Root.Children.Add(_icon);
            Root.Children.Add(text);
            Root.Children.Add(_number);
            if (force is not null) _icon.Foreground = force;
        }

        public void Set(DeviceViewModel vm, string? iconPath, Brush? number, Brush? detail)
        {
            if (iconPath is not null) _icon.Content = DeviceMenu.VectorIcon(iconPath, 24);
            _name.Text = vm.Name;
            _detail.Text = vm.CompactDetail;
            _value.Text = vm.NumberText;
            _unit.Text = vm.HasNumber ? "%" : "";
            _value.FontSize = vm.HasNumber ? 24 : 16;
            var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            var primary = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            _name.Foreground = _force ?? primary;
            _detail.Foreground = _force ?? detail ?? secondary;
            _number.Foreground = _force ?? number ?? primary;
        }
    }
}
