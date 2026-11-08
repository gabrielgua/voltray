using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Voltray.Rendering;
using Voltray.Core;
using Voltray.Core.Icons;
using Voltray.Ipc;
using Voltray.Settings.ViewModels;

namespace Voltray.Settings.Pages;

public static class DeviceMenu
{
    /// Device menu (opened by clicking its card): tray, notifications, category and tray icon.
    public static void Show(DeviceViewModel vm, FrameworkElement button)
    {
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
            var item = new MenuFlyoutItem { Text = Categories.Label(c), Icon = VectorIcon(CategoryIcons.For(c), 16) };
            MarkSelected(item, !vm.IsCategoryAutomatic && vm.Category == c);
            item.Click += (_, _) => _ = App.Host.SetCategoryAsync(vm.Id, c);
            category.Items.Add(item);
        }

        // Tray icon for this device (used when the tray shows one icon per device): opens the picker grid.
        var icon = new MenuFlyoutItem { Text = "Ícone na bandeja…", Icon = new FontIcon { Glyph = "" } };
        icon.Click += (_, _) => ShowIconPicker(vm, button);
        var notify = new ToggleMenuFlyoutItem { Text = "Notificações de bateria", IsChecked = vm.Notify, Icon = new FontIcon { Glyph = "\uEA8F" } };
        notify.Click += (_, _) =>
        {
            bool on = notify.IsChecked;
            _ = App.Host.ChangeSettingsAsync(s => { if (on) s.MutedDevices.Remove(vm.Id); else s.MutedDevices.Add(vm.Id); });
        };
        var tray = new ToggleMenuFlyoutItem { Text = "Mostrar na bandeja", IsChecked = vm.ShownInTray, Icon = new FontIcon { Glyph = "" } };
        tray.Click += (_, _) => vm.ShownInTray = tray.IsChecked;
        var menu = new MenuFlyout();
        menu.Items.Add(tray);
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

    /// A Hugeicons symbol (same as the tray icons) as vector geometry in the current text colour,
    /// so it stays sharp at any scale and follows the theme.
    public static IconElement VectorIcon(string path, double size)
    {
        try
        {
            return new PathIcon
            {
                Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), IconOutline.Markup(path, size, size <= 20 ? 1.75 : 1.5)),
                Width = size, Height = size,
            };
        }
        catch (Exception) { return new FontIcon { Glyph = "", FontSize = size * 0.75 }; } // never break the menu over an icon
    }

    /// Grid of tray icons for one device, anchored to its "..." button.
    static void ShowIconPicker(DeviceViewModel vm, FrameworkElement anchor)
    {
        var settings = App.Host.State?.Settings;
        string? current = settings?.DeviceIcons.GetValueOrDefault(vm.Id);
        string selectedLabel = current is not null && DeviceIcons.Find(current) is { } chosen ? chosen.Label : "Da categoria";
        var hover = new TextBlock { Text = selectedLabel, Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 340,
            Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, // 5 tiles of 60 + 4 spacing fill the 324 panel
        };
        AutomationProperties.SetName(grid, "Ícones");
        // Not constrained to the window: the compact window is narrower and shorter than the picker.
        var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom, ShouldConstrainToRootBounds = false };

        // (key, label, path): key null = the category's icon
        var options = new List<(string? Key, string Label, string Path)> { (null, "Da categoria", CategoryIcons.For(vm.Category)) };
        options.AddRange(DeviceIcons.All.Select(e => ((string?)e.Key, e.Label, e.Path)));

        void Fill(string query)
        {
            grid.Items.Clear();
            foreach (var o in options.Where(o => query.Length == 0 || o.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                                 || (o.Key?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                var tile = new Grid { Width = 60, Height = 60, Tag = o.Key ?? "" };
                tile.Children.Add(new Viewbox { Width = 28, Height = 28, Child = VectorIcon(o.Path, 28) });
                if (o.Key is null)
                    tile.Children.Add(new TextBlock
                    {
                        Text = "auto", FontSize = 10, Margin = new Thickness(0, 0, 0, 2),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    });
                ToolTipService.SetToolTip(tile, o.Label);
                AutomationProperties.SetName(tile, o.Label);
                string label = o.Label;
                tile.PointerEntered += (_, _) => hover.Text = label;
                tile.PointerExited += (_, _) => hover.Text = selectedLabel;
                tile.GotFocus += (_, _) => hover.Text = label;
                grid.Items.Add(tile);
                if (o.Key == current) grid.SelectedItem = tile;
            }
        }

        grid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not Grid { Tag: string key }) return;
            _ = App.Host.ChangeSettingsAsync(s => { if (key.Length == 0) s.DeviceIcons.Remove(vm.Id); else s.DeviceIcons[vm.Id] = key; });
            flyout.Hide();
        };

        var search = new TextBox { PlaceholderText = "Buscar ícone" };
        AutomationProperties.SetName(search, "Buscar ícone");
        search.TextChanged += (_, _) => Fill(search.Text.Trim());
        search.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Down && grid.Items.Count > 0) { grid.Focus(FocusState.Keyboard); e.Handled = true; } };

        var panel = new StackPanel { Width = 324, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Ícone na bandeja", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        if (settings?.TrayMode == TrayMode.PerCategory)
            panel.Children.Add(new TextBlock
            {
                Text = "Aparece no modo \"um ícone por dispositivo\".", TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            });
        panel.Children.Add(search);
        panel.Children.Add(grid);
        panel.Children.Add(hover);
        flyout.Content = panel;
        Fill("");
        flyout.Opened += (_, _) => (grid.SelectedItem is UIElement sel ? grid.ContainerFromItem(sel) as Control : null)?.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
    }
}
