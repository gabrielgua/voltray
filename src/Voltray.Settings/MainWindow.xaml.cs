using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Voltray.Settings.Pages;
using Voltray.Settings.Services;

namespace Voltray.Settings;

public sealed partial class MainWindow : Window
{
    /// The tray host finds this window by title to bring it forward instead of starting a second one.
    public const string WindowTitle = "Voltray";

    readonly Dictionary<string, Func<UIElement>> _pages = new()
    {
        ["devices"] = () => new DevicesPage(),
        ["settings"] = () => new SettingsPage(),
        ["about"] = () => new AboutPage(),
    };

    public MainWindow(string startPage)
    {
        InitializeComponent();
        Title = WindowTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "voltray.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scale(980), Scale(700)));
        if (AppWindow.Presenter is OverlappedPresenter p) { p.PreferredMinimumWidth = Scale(640); p.PreferredMinimumHeight = Scale(480); }

        HostInfoBar.IsOpen = false; // shown only if the host does not answer
        App.Host.ConnectionChanged += connected => HostInfoBar.IsOpen = !connected;
        _ = ShowHostWarningIfNoConnectionAsync();

        Select(_pages.ContainsKey(startPage) ? startPage : "devices");
    }

    /// AppWindow sizes are in physical pixels; the XAML root is not ready yet, so ask Win32.
    int Scale(int dip) => dip * (int)GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    async Task ShowHostWarningIfNoConnectionAsync()
    {
        await Task.Delay(2500);
        if (!App.Host.IsConnected) HostInfoBar.IsOpen = true;
    }

    void Select(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
        Nav.SelectedItem = item; // raises SelectionChanged → shows the page
    }

    void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && _pages.TryGetValue(tag, out var create))
            PageHost.Content = create();
    }

    void OnStartHostClick(object sender, RoutedEventArgs e) => HostConnection.StartHost();
}
