using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Settings.ViewModels;

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

    /// "..." menu: change the category (or go back to automatic).
    void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceViewModel vm } button) return;
        var category = new MenuFlyoutSubItem { Text = "Categoria", Icon = new FontIcon { Glyph = "" } };
        var auto = new RadioMenuFlyoutItem { Text = "Automática", GroupName = "cat", IsChecked = vm.IsCategoryAutomatic };
        auto.Click += (_, _) => _ = App.Host.SetCategoryAsync(vm.Id, null);
        category.Items.Add(auto);
        category.Items.Add(new MenuFlyoutSeparator());
        foreach (var c in Categories.All)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = Categories.Label(c), GroupName = "cat", IsChecked = !vm.IsCategoryAutomatic && vm.Category == c,
                Icon = new FontIcon { Glyph = Categories.Glyph(c) },
            };
            item.Click += (_, _) => _ = App.Host.SetCategoryAsync(vm.Id, c);
            category.Items.Add(item);
        }
        var menu = new MenuFlyout();
        menu.Items.Add(category);
        menu.ShowAt(button);
    }
}
