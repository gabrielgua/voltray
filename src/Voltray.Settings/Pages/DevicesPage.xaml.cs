using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Voltray.Core;
using Voltray.Core.Icons;
using Voltray.Ipc;
using Voltray.Settings.ViewModels;
using Windows.Storage.Streams;

namespace Voltray.Settings.Pages;

public sealed partial class DevicesPage : UserControl
{
    readonly ObservableCollection<CategoryGroup> _groups = [];
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };

    public DevicesPage()
    {
        InitializeComponent();
        GroupList.ItemsSource = _groups;
        App.Host.StateReceived += Apply;
        if (App.Host.State is { } s) Apply(s);
        _clock.Tick += (_, _) => { foreach (var d in _groups.SelectMany(g => g.Devices)) d.RefreshText(); }; // "há N min"
        _clock.Start();
        Unloaded += (_, _) => { App.Host.StateReceived -= Apply; _clock.Stop(); };
    }

    /// Syncs groups and cards with the host state in place (no rebuild, so focus and toggles survive).
    void Apply(StateDto state)
    {
        var byCategory = state.Devices.GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var g in _groups.Where(g => !byCategory.ContainsKey(g.Category)).ToList()) _groups.Remove(g);
        foreach (var category in Categories.All.Where(byCategory.ContainsKey))
        {
            var group = _groups.FirstOrDefault(g => g.Category == category);
            if (group is null)
            {
                group = new CategoryGroup(category);
                int index = _groups.Count(g => g.Category < category);
                _groups.Insert(index, group);
            }
            var dtos = byCategory[category];
            foreach (var gone in group.Devices.Where(d => dtos.All(x => x.Id != d.Id)).ToList()) group.Devices.Remove(gone);
            for (int i = 0; i < dtos.Count; i++)
            {
                var vm = group.Devices.FirstOrDefault(d => d.Id == dtos[i].Id);
                if (vm is null)
                {
                    vm = new DeviceViewModel(dtos[i]);
                    vm.ShownInTrayChanged += OnShownInTrayChanged;
                    group.Devices.Insert(i, vm);
                }
                else
                {
                    vm.Update(dtos[i]);
                    int at = group.Devices.IndexOf(vm);
                    if (at != i) group.Devices.Move(at, i);
                }
            }
        }
        EmptyState.Visibility = state.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnShownInTrayChanged(DeviceViewModel vm, bool shown) =>
        _ = App.Host.ChangeSettingsAsync(s => { if (shown) s.HiddenDevices.Remove(vm.Id); else s.HiddenDevices.Add(vm.Id); });

    void OnRefreshClick(object sender, RoutedEventArgs e) => _ = App.Host.RefreshAsync();

    /// "..." menu: notifications for this device, and its category (or back to automatic).
    void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceViewModel vm } button) return;
        var category = new MenuFlyoutSubItem { Text = "Categoria", Icon = new FontIcon { Glyph = "" } };
        // Plain items with a ✓ on the right for the current choice: radio items would add an
        // empty marker column that pushes every icon and label to the right.
        var auto = new MenuFlyoutItem { Text = "Automática" };
        MarkSelected(auto, vm.IsCategoryAutomatic);
        auto.Click += (_, _) => _ = App.Host.SetCategoryAsync(vm.Id, null);
        category.Items.Add(auto);
        category.Items.Add(new MenuFlyoutSeparator());
        foreach (var c in Categories.All)
        {
            var item = new MenuFlyoutItem { Text = Categories.Label(c), Icon = SvgIcon(CategoryIcons.For(c), button) };
            MarkSelected(item, !vm.IsCategoryAutomatic && vm.Category == c);
            item.Click += (_, _) => _ = App.Host.SetCategoryAsync(vm.Id, c);
            category.Items.Add(item);
        }

        // Tray icon for this device (used when the tray shows one icon per device).
        var settings = App.Host.State?.Settings;
        string? current = settings?.DeviceIcons.GetValueOrDefault(vm.Id);
        var icon = new MenuFlyoutSubItem { Text = "Ícone na bandeja", Icon = new FontIcon { Glyph = "" } };
        var byCategory = new MenuFlyoutItem { Text = "Da categoria", Icon = SvgIcon(CategoryIcons.For(vm.Category), button) };
        MarkSelected(byCategory, current is null);
        byCategory.Click += (_, _) => _ = App.Host.ChangeSettingsAsync(s => s.DeviceIcons.Remove(vm.Id));
        icon.Items.Add(byCategory);
        icon.Items.Add(new MenuFlyoutSeparator());
        foreach (var entry in DeviceIcons.All)
        {
            var item = new MenuFlyoutItem { Text = entry.Label, Icon = SvgIcon(entry.Path, button) };
            MarkSelected(item, current == entry.Key);
            item.Click += (_, _) => _ = App.Host.ChangeSettingsAsync(s => s.DeviceIcons[vm.Id] = entry.Key);
            icon.Items.Add(item);
        }
        if (settings?.TrayMode == TrayMode.PerCategory)
        {
            icon.Items.Add(new MenuFlyoutSeparator());
            icon.Items.Add(new MenuFlyoutItem { Text = "Aparece no modo \"um ícone por dispositivo\"", IsEnabled = false });
        }
        var notify = new ToggleMenuFlyoutItem { Text = "Notificações de bateria", IsChecked = vm.Notify, Icon = new FontIcon { Glyph = "\uEA8F" } };
        notify.Click += (_, _) =>
        {
            bool on = notify.IsChecked;
            _ = App.Host.ChangeSettingsAsync(s => { if (on) s.MutedDevices.Remove(vm.Id); else s.MutedDevices.Add(vm.Id); });
        };
        var menu = new MenuFlyout();
        menu.Items.Add(notify);
        menu.Items.Add(category);
        menu.Items.Add(icon);
        menu.ShowAt(button);
    }

    static void MarkSelected(MenuFlyoutItem item, bool selected)
    {
        if (!selected) return;
        item.KeyboardAcceleratorTextOverride = "✓";
        AutomationProperties.SetItemStatus(item, "Selecionada");
    }

    /// A Hugeicons symbol (same as the tray icons) as an SVG in the menu's text colour.
    static ImageIcon SvgIcon(string path, FrameworkElement themeOf)
    {
        string color = themeOf.ActualTheme == ElementTheme.Light ? "#1A1A1A" : "#FFFFFF";
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{color}\" " +
                     $"stroke-width=\"{CategoryIcons.Stroke}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"{path}\"/></svg>";
        var source = new SvgImageSource();
        _ = LoadSvgAsync(source, svg);
        return new ImageIcon { Source = source };
    }

    static async Task LoadSvgAsync(SvgImageSource source, string svg)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteString(svg);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        await source.SetSourceAsync(stream);
    }
}
