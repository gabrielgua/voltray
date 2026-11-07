using System.IO.Compression;
using Voltray.Core;
using static Voltray.Interop.Win32;

namespace Voltray.Tray;

public enum IconStyle { Percentage, Glyph }

/// Draws tray icons. Text and glyphs are rendered large with GDI grayscale antialiasing, cropped
/// to their ink box and area-resampled into the target size, so they fill the icon at any DPI.
internal static unsafe class IconRenderer
{
    const int SrcW = 640, SrcH = 256;

    public readonly record struct Rgb(byte R, byte G, byte B);

    /// Segoe Fluent Icons (Windows 11), falling back to Segoe MDL2 Assets on Windows 10.
    static string GlyphFor(DeviceCategory c) => c switch
    {
        DeviceCategory.Mouse => "",
        DeviceCategory.Keyboard => "",
        DeviceCategory.Headphones => "",
        DeviceCategory.Earbuds => "",
        DeviceCategory.Controller => "",
        _ => "",
    };

    public static Rgb Foreground(bool lightTaskbar) => lightTaskbar ? new(26, 26, 26) : new(255, 255, 255);

    public static Rgb LevelColor(int? pct, bool charging, bool lightTaskbar)
    {
        if (pct is null) return lightTaskbar ? new(120, 120, 120) : new(150, 150, 150);
        if (charging) return lightTaskbar ? new(15, 123, 15) : new(108, 203, 95);
        if (pct <= 10) return lightTaskbar ? new(196, 43, 28) : new(255, 99, 71);
        if (pct <= 20) return lightTaskbar ? new(157, 93, 0) : new(252, 185, 0);
        return Foreground(lightTaskbar);
    }

    public static string TextFor(int? pct) => pct switch { null => "–", >= 100 => "F", _ => pct.Value.ToString() };

    /// Straight-alpha BGRA pixels, size×size.
    public static uint[] Render(IconStyle style, DeviceCategory category, int? pct, bool charging, bool lightTaskbar, int size)
    {
        var px = new uint[size * size];
        if (style == IconStyle.Percentage)
        {
            var cov = Coverage(TextFor(pct), "Bahnschrift", 600, size, size, stretch: true);
            Paint(px, cov, LevelColor(pct, charging, lightTaskbar));
            return px;
        }

        // Glyph on top, level bar along the bottom edge.
        int bar = Math.Max(2, size / 8), gap = Math.Max(1, size / 16);
        int glyphH = size - bar - gap;
        var glyph = Coverage(GlyphFor(category), FluentFont, 400, size, glyphH, stretch: false);
        Paint(px, glyph, Foreground(lightTaskbar));

        var fill = LevelColor(pct, charging, lightTaskbar);
        var track = lightTaskbar ? new Rgb(26, 26, 26) : new Rgb(255, 255, 255);
        int filled = pct is int p ? Math.Max(1, (int)Math.Round(size * Math.Clamp(p, 0, 100) / 100.0)) : 0;
        for (int y = size - bar; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = x < filled ? Bgra(fill, 255) : Bgra(track, 70);
        return px;
    }

    static string? _fluentFont;
    static string FluentFont => _fluentFont ??= Environment.OSVersion.Version.Build >= 22000 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static uint Bgra(Rgb c, int a) => (uint)a << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    static void Paint(uint[] px, byte[] cov, Rgb c)
    {
        for (int i = 0; i < cov.Length; i++) if (cov[i] > 0) px[i] = Bgra(c, cov[i]);
    }

    /// Coverage map of <paramref name="text"/> fitted into the top <paramref name="h"/> rows of a size×size icon.
    /// stretch: fill the full height using the digit "8" as reference (digits); otherwise keep aspect (glyphs).
    static byte[] Coverage(string text, string font, int weight, int size, int h, bool stretch)
    {
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = SrcW, biHeight = -SrcH, biPlanes = 1, biBitCount = 32 };
        nint dc = CreateCompatibleDC(0);
        nint bmp = CreateDIBSection(dc, ref bi, 0, out byte* bits, 0, 0);
        nint oldBmp = SelectObject(dc, bmp);
        nint hfont = CreateFont(-200, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, font);
        nint oldFont = SelectObject(dc, hfont);
        SetBkMode(dc, 1);
        SetTextColor(dc, 0x00FFFFFF);

        int x0, y0, x1, y1;
        if (stretch)
        {
            (_, y0, _, y1) = Ink(dc, bits, "8"); // so "–" keeps its proportions
            (x0, _, x1, _) = Ink(dc, bits, text);
        }
        else (x0, y0, x1, y1) = Ink(dc, bits, text);

        var cov = new byte[size * size];
        if (x1 >= 0)
        {
            double srcW = x1 - x0 + 1, srcH = y1 - y0 + 1;
            double dstH = h, dstW = srcW * dstH / srcH;
            if (dstW > size) { dstW = size; if (!stretch) dstH = srcH * dstW / srcW; }
            double offX = (size - dstW) / 2, offY = (h - dstH) / 2;
            double sx = srcW / dstW, sy = srcH / dstH;
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < size; tx++)
                {
                    double fx0 = (tx - offX) * sx + x0, fy0 = (ty - offY) * sy + y0;
                    int ix0 = Math.Max((int)Math.Floor(fx0), x0), ix1 = Math.Min((int)Math.Ceiling(fx0 + sx), x1 + 1);
                    int iy0 = Math.Max((int)Math.Floor(fy0), y0), iy1 = Math.Min((int)Math.Ceiling(fy0 + sy), y1 + 1);
                    if (ix1 <= ix0 || iy1 <= iy0) continue;
                    long sum = 0;
                    for (int y = iy0; y < iy1; y++) for (int x = ix0; x < ix1; x++) sum += bits[(y * SrcW + x) * 4 + 2];
                    double avg = sum / (double)((ix1 - ix0) * (iy1 - iy0));
                    cov[ty * size + tx] = (byte)Math.Min(255, avg * 1.15); // thin strokes read better slightly boosted
                }
        }

        SelectObject(dc, oldFont); DeleteObject(hfont);
        SelectObject(dc, oldBmp); DeleteObject(bmp); DeleteDC(dc);
        return cov;
    }

    /// Draws text centered in the scratch DIB and returns its ink bounding box (x0, y0, x1, y1).
    static (int, int, int, int) Ink(nint dc, byte* bits, string text)
    {
        new Span<byte>(bits, SrcW * SrcH * 4).Clear();
        var rc = new RECT { Right = SrcW, Bottom = SrcH };
        DrawText(dc, text, -1, ref rc, 0x1 | 0x4 | 0x20); // center, vcenter, singleline
        GdiFlush();
        int x0 = SrcW, y0 = SrcH, x1 = -1, y1 = -1;
        for (int y = 0; y < SrcH; y++)
            for (int x = 0; x < SrcW; x++)
                if (bits[(y * SrcW + x) * 4 + 2] > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        return (x0, y0, x1, y1);
    }

    public static nint CreateHIcon(uint[] px, int size)
    {
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        nint color = CreateDIBSection(0, ref bi, 0, out byte* bits, 0, 0);
        px.AsSpan().CopyTo(new Span<uint>(bits, px.Length));
        nint mask = CreateBitmap(size, size, 1, 1, 0);
        var ii = new ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
        nint icon = CreateIconIndirect(ref ii);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    /// Contact sheet of every style, size and theme (`Voltray.exe --icon-preview file.png`), for docs and review.
    public static void WritePreview(string path)
    {
        var cases = new (IconStyle, DeviceCategory, int?, bool)[]
        {
            (IconStyle.Percentage, DeviceCategory.Mouse, 66, false), (IconStyle.Percentage, DeviceCategory.Mouse, 100, false),
            (IconStyle.Percentage, DeviceCategory.Mouse, 18, false), (IconStyle.Percentage, DeviceCategory.Mouse, 45, true),
            (IconStyle.Percentage, DeviceCategory.Mouse, null, false),
            (IconStyle.Glyph, DeviceCategory.Mouse, 66, false), (IconStyle.Glyph, DeviceCategory.Keyboard, 94, false),
            (IconStyle.Glyph, DeviceCategory.Headphones, 18, false), (IconStyle.Glyph, DeviceCategory.Controller, 7, false),
            (IconStyle.Glyph, DeviceCategory.Other, 45, true),
        };
        int[] sizes = [16, 20, 24, 32];
        const int zoom = 5, cell = 32 * zoom + 20;
        int w = cases.Length * cell, h = sizes.Length * 2 * cell;
        var sheet = new Rgb[w * h];
        int row = 0;
        foreach (int size in sizes)
            foreach (bool light in new[] { false, true })
            {
                Rgb bg = light ? new(243, 243, 243) : new(32, 32, 32); // Windows 11 taskbar
                for (int c = 0; c < cases.Length; c++)
                {
                    var (style, cat, pct, chg) = cases[c];
                    var px = Render(style, cat, pct, chg, light, size);
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            int ix = (x - 10) / zoom, iy = (y - 10) / zoom;
                            uint p = x >= 10 && y >= 10 && ix < size && iy < size ? px[iy * size + ix] : 0;
                            int a = (int)(p >> 24);
                            byte Mix(int fg, int b) => (byte)((fg * a + b * (255 - a)) / 255);
                            sheet[(row * cell + y) * w + c * cell + x] = new(Mix((int)(p >> 16 & 255), bg.R), Mix((int)(p >> 8 & 255), bg.G), Mix((int)(p & 255), bg.B));
                        }
                }
                row++;
            }
        WritePng(path, sheet, w, h);
    }

    static void WritePng(string path, Rgb[] px, int w, int h)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < w; x++) { var p = px[y * w + x]; raw.WriteByte(p.R); raw.WriteByte(p.G); raw.WriteByte(p.B); }
        }
        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var hdr = new byte[13];
        BE(hdr, 0, w); BE(hdr, 4, h); hdr[8] = 8; hdr[9] = 2;
        Chunk(f, "IHDR", hdr);
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", []);
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, td); data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, (int)Crc(td)); s.Write(crc);
    }

    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in d) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        return ~c;
    }
}
