using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Rendering;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Voltray;

/// Native Windows toast notifications with the device's icon, and an "urgent" priority that can show
/// during Do Not Disturb (if the user allows it for Voltray). Unpackaged apps need an AppUserModelID
/// registered under HKCU; when anything fails the caller falls back to the tray balloon.
internal static partial class Toasts
{
    const string AppId = "Voltray.BatteryMonitor";
    static readonly string ImageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltray", "toast");

    static ToastNotifier? _notifier;

    /// Raised (on a background thread) when the user clicks a notification.
    public static event Action? Clicked;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void Initialize()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
            {
                key.SetValue("DisplayName", "Voltray");
                var icon = Path.Combine(AppContext.BaseDirectory, "voltray-256.png");
                if (File.Exists(icon)) key.SetValue("IconUri", icon);
            }
            SetCurrentProcessExplicitAppUserModelID(AppId);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        }
        catch (Exception e) { Log.Error("toast notifications unavailable, using tray balloons", e); _notifier = null; }
    }

    /// False when toasts are not available; the caller then uses the tray balloon.
    public static bool Show(string title, string text, DeviceCategory category, string? iconKey, int? percent, bool charging, bool urgent)
    {
        if (_notifier is null) return false;
        try
        {
            string? image = DeviceImage(category, iconKey, percent, charging);
            // "urgent" is a Windows 11 scenario; older builds would reject the whole toast.
            bool urgentScenario = urgent && Environment.OSVersion.Version.Build >= 22000;
            string xml =
                $"<toast launch=\"open\"{(urgentScenario ? " scenario=\"urgent\"" : "")}>" +
                "<visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text>" +
                (image != null ? $"<image placement=\"appLogoOverride\" src=\"{SecurityElement.Escape(new Uri(image).AbsoluteUri)}\"/>" : "") +
                "</binding></visual></toast>";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var toast = new ToastNotification(doc) { Priority = urgent ? ToastNotificationPriority.High : ToastNotificationPriority.Default };
            toast.Activated += (_, _) => Clicked?.Invoke();
            _notifier.Show(toast);
            Log.Info($"toast shown{(urgent ? " (urgent)" : "")}: {title} — {text}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error("showing toast", e);
            return false;
        }
    }

    /// The device's symbol coloured by its level, rendered once per (icon, colour step, theme).
    static string? DeviceImage(DeviceCategory category, string? iconKey, int? percent, bool charging)
    {
        try
        {
            bool light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 1;
            int bucket = percent switch { null => -1, > 50 => 3, > 20 => 2, > 10 => 1, _ => 0 };
            string name = $"{iconKey ?? category.ToString()}-{(charging ? "c" : bucket.ToString())}-{(light ? "l" : "d")}.png";
            string path = Path.Combine(ImageDir, name);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(ImageDir);
                const int size = 96;
                var px = IconRenderer.Render(IconStyle.GlyphColorSteps, category, percent, charging, light, size, new IconOptions(IconKey: iconKey));
                IconRenderer.SavePng(px, size, path);
            }
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("toast image", e); return null; }
    }
}
