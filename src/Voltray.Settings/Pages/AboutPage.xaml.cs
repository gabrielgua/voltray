using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Voltray.Settings.Pages;

public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        string version = App.Host.State?.Version ?? typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = $"Versão {version}";
    }

    void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltray");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
    }
}
