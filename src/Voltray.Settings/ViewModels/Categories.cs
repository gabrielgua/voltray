using System.Collections.ObjectModel;
using Voltray.Core;

namespace Voltray.Settings.ViewModels;

public static class Categories
{
    public static readonly DeviceCategory[] All = Enum.GetValues<DeviceCategory>();

    public static string Label(DeviceCategory c) => c switch
    {
        DeviceCategory.Mouse => "Mouse",
        DeviceCategory.Keyboard => "Teclado",
        DeviceCategory.Headphones => "Headphones",
        DeviceCategory.Earbuds => "Earbuds",
        DeviceCategory.Controller => "Controle",
        _ => "Outros",
    };

    /// Segoe Fluent Icons, same glyphs as the tray icons.
    public static string Glyph(DeviceCategory c) => c switch
    {
        DeviceCategory.Mouse => "",
        DeviceCategory.Keyboard => "",
        DeviceCategory.Headphones or DeviceCategory.Earbuds => "",
        DeviceCategory.Controller => "",
        _ => "",
    };
}

/// A category section on the devices page.
public sealed partial class CategoryGroup(DeviceCategory category)
{
    public DeviceCategory Category { get; } = category;
    public string Title => Categories.Label(Category);
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
}
