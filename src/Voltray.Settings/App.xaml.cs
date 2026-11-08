using Microsoft.UI.Xaml;
using Voltray.Core;
using Voltray.Settings.Services;

namespace Voltray.Settings;

public partial class App : Application
{
    public static HostConnection Host { get; private set; } = null!;
    /// --demo [Stream|Bubbles|Sparks]: show sample devices instead of the host's (screenshots, trying animations).
    public static bool Demo { get; } = Environment.GetCommandLineArgs().Contains("--demo");

    MainWindow? _window;

    public App()
    {
        InitializeComponent();
        if (Environment.GetCommandLineArgs() is var a && Array.IndexOf(a, "--theme") is int t && t >= 0 && t + 1 < a.Length) // screenshots
            RequestedTheme = a[t + 1] == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
        UnhandledException += (_, e) => Log.Error("settings window", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Host = new HostConnection(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        var cmd = Environment.GetCommandLineArgs();
        _window = new MainWindow(Arg(cmd, "--page") ?? "devices");
        Host.Start();

        // Test/automation flags: place the window (physical px, e.g. on a second monitor) and/or
        // show it without stealing focus from whatever the user is doing.
        if (Arg(cmd, "--at") is { } at && at.Split(',') is [var x, var y] && int.TryParse(x, out int px) && int.TryParse(y, out int py))
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(px, py));
        if (cmd.Contains("--no-activate")) _window.AppWindow.Show(activateWindow: false);
        else _window.Activate();
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
