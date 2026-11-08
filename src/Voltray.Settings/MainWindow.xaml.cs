using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Voltray.Settings.Pages;
using Voltray.Settings.Services;

namespace Voltray.Settings;

/// A compact window whose main view is the device list; settings and about open in the same
/// window (wider) behind the gear button, with a back button.
public sealed partial class MainWindow : Window
{
    /// The tray host finds this window by title to bring it forward instead of starting a second one.
    public const string WindowTitle = "Voltray";

    const int CompactWidth = 380, WideWidth = 760, WideHeight = 680;
    // Title bar + header + footer, and one card plus spacing, in DIPs (for fitting the list's height).
    const int Chrome = 32 + 48 + 40 + 24, RowHeight = 62;

    string _view = "";
    int _deviceCount;
    DeviceListView? _list;

    public MainWindow(string startPage)
    {
        InitializeComponent();
        Title = WindowTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "voltray.ico"));
        if (AppWindow.Presenter is OverlappedPresenter p) { p.PreferredMinimumWidth = Scale(340); p.PreferredMinimumHeight = Scale(260); p.IsMaximizable = false; }

        HostInfoBar.IsOpen = false; // shown only if the host does not answer
        App.Host.ConnectionChanged += connected => HostInfoBar.IsOpen = !connected;
        _ = ShowHostWarningIfNoConnectionAsync();

        Show(startPage is "settings" or "about" ? startPage : "devices");
    }

    /// AppWindow sizes are in physical pixels; the XAML root may not be ready yet, so ask Win32.
    int Scale(int dip) => dip * (int)GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    async Task ShowHostWarningIfNoConnectionAsync()
    {
        await Task.Delay(2500);
        if (!App.Host.IsConnected) HostInfoBar.IsOpen = true;
    }

    void Show(string view)
    {
        if (view == _view) return;
        bool resize = _view is "devices" or ""; // leaving the compact list, or first show
        _view = view;
        switch (view)
        {
            case "devices":
                if (_list is null)
                {
                    _list = new DeviceListView();
                    _list.SettingsRequested += () => Show("settings");
                    _list.CountChanged += n => { _deviceCount = n; if (_view == "devices") FitCompact(); };
                }
                PageHost.Content = _list;
                SubHeader.Visibility = Visibility.Collapsed;
                FitCompact();
                break;
            case "settings":
                PageHost.Content = new SettingsPage();
                SubTitle.Text = "Configurações";
                AboutLink.Visibility = Visibility.Visible;
                SubHeader.Visibility = Visibility.Visible;
                if (resize) Resize(WideWidth, WideHeight);
                break;
            case "about":
                PageHost.Content = new AboutPage();
                SubTitle.Text = "Sobre";
                AboutLink.Visibility = Visibility.Collapsed;
                SubHeader.Visibility = Visibility.Visible;
                if (resize) Resize(WideWidth, WideHeight);
                break;
        }
    }

    /// Compact width, tall enough for every device up to a limit (the list scrolls past it).
    void FitCompact() => Resize(CompactWidth, Math.Clamp(Chrome + Math.Max(_deviceCount, 4) * RowHeight, 440, 720));

    /// Resizes keeping the top-left corner where it is, but shifted back inside the monitor's work
    /// area (a window near the right edge or the taskbar would otherwise grow off screen).
    void Resize(int w, int h)
    {
        int pw = Scale(w), ph = Scale(h);
        var pos = AppWindow.Position;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int x = Math.Max(work.X, Math.Min(pos.X, work.X + work.Width - pw));
        int y = Math.Max(work.Y, Math.Min(pos.Y, work.Y + work.Height - ph));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, pw, ph));
    }

    void OnBackClick(object sender, RoutedEventArgs e) => Show(_view == "about" ? "settings" : "devices");
    void OnAboutClick(object sender, RoutedEventArgs e) => Show("about");
    void OnStartHostClick(object sender, RoutedEventArgs e) => HostConnection.StartHost();
}
