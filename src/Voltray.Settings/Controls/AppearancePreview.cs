using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Voltray.Core;
using Voltray.Core.Icons;
using Voltray.Ipc;
using Voltray.Rendering;

namespace Voltray.Settings.Controls;

/// Pictures of how the batteries look on the taskbar, drawn by the same renderers the tray app
/// uses, with three sample devices (headphones 93 %, mouse 45 % charging, keyboard 12 %).
public static class AppearancePreview
{
    static readonly (DeviceCategory Category, int Percent, bool Charging)[] Samples =
        [(DeviceCategory.Headphones, 93, false), (DeviceCategory.Mouse, 45, true), (DeviceCategory.Keyboard, 12, false)];

    public const double StripHeight = 48; // a Windows 11 taskbar

    /// The taskbar's colour scheme (it can differ from the apps' light/dark setting).
    public static bool LightTaskbar() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    public static Windows.UI.Color TaskbarColor(bool light) => light ? Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3) : Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1C);

    /// Text on the taskbar in <paramref name="style"/>.
    public static (WriteableBitmap Image, double Width) Taskbar(TaskbarStyle style, bool light, double scale, int count = 3)
    {
        var devices = Samples.Take(count).Select(s => new TaskbarDevice(CategoryIcons.For(s.Category), s.Percent, s.Charging)).ToList();
        var frame = TaskbarRenderer.Render(devices, style, light, (float)scale, (int)Math.Round(StripHeight * scale));
        return (ToBitmap(frame.Pixels, frame.Width, frame.Height), frame.Width / scale);
    }

    /// Tray icons in <paramref name="style"/>, at their real size and spacing.
    public static (WriteableBitmap Image, double Width) Tray(IconStyle style, bool light, double scale, bool percentSign, bool colorNumber)
    {
        int icon = (int)Math.Round(16 * scale), slot = (int)Math.Round(28 * scale), h = (int)Math.Round(StripHeight * scale);
        int w = slot * Samples.Length;
        var px = new byte[w * h * 4];
        for (int i = 0; i < Samples.Length; i++)
        {
            var (category, pct, charging) = Samples[i];
            var src = IconRenderer.Render(style, category, pct, charging, light, icon, new IconOptions(percentSign, colorNumber));
            int ox = i * slot + (slot - icon) / 2, oy = (h - icon) / 2;
            for (int y = 0; y < icon; y++)
                for (int x = 0; x < icon; x++)
                {
                    uint c = src[y * icon + x];
                    int a = (int)(c >> 24), o = ((oy + y) * w + ox + x) * 4;
                    // straight alpha -> premultiplied BGRA
                    px[o] = (byte)((c & 0xFF) * a / 255);
                    px[o + 1] = (byte)((c >> 8 & 0xFF) * a / 255);
                    px[o + 2] = (byte)((c >> 16 & 0xFF) * a / 255);
                    px[o + 3] = (byte)a;
                }
        }
        return (ToBitmap(px, w, h), w / scale);
    }

    static WriteableBitmap ToBitmap(byte[] premultipliedBgra, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h);
        premultipliedBgra.CopyTo(bmp.PixelBuffer);
        bmp.Invalidate();
        return bmp;
    }
}
