using System.IO.Compression;
using Voltray.Core;
using Voltray.Core.Icons;
using Voltray.Ipc;
using static Voltray.Rendering.Gdi;

namespace Voltray.Rendering;

/// <param name="PercentSign">Show "%" after the number (number style).</param>
/// <param name="ColorNumber">Tint the number by level instead of the taskbar text colour.</param>
/// <param name="IconKey">Icon picked for the device (Voltray.Core.Icons.DeviceIcons key); null = category icon.</param>
/// <param name="Dimmed">Faded, for a device that is disconnected but still shown.</param>
public readonly record struct IconOptions(bool PercentSign = true, bool ColorNumber = false, string? IconKey = null, bool Dimmed = false);

/// Draws Voltray's tray icons. Used by the tray host and by the settings window's style previews,
/// so both show exactly the same pixels. Symbols are Hugeicons SVG paths stroked large with GDI and
/// area-resampled down (antialiasing); digits use pixel fonts drawn on the pixel grid, which stay
/// readable at 16 px where scaled vector digits blur. Tray icons are always square.
public static unsafe partial class IconRenderer
{
    const int SrcW = 640, SrcH = 256;

    public readonly record struct Rgb(byte R, byte G, byte B);

    /// Stroke for tray symbols: a little heavier than the 1.5 design stroke so 16 px stays legible.
    const double TrayStroke = 1.75;

    public static Rgb Foreground(bool lightTaskbar) => lightTaskbar ? new(26, 26, 26) : new(255, 255, 255);

    static Rgb Muted(bool light) => light ? new(120, 120, 120) : new(150, 150, 150);

    /// Charging green, otherwise the four level steps.
    public static Rgb LevelColor(int pct, bool charging, bool light) => charging
        ? (light ? new(0, 120, 106) : new(45, 212, 191)) // brand teal
        : pct switch
        {
            > 50 => light ? new(22, 128, 61) : new(74, 222, 128),
            > 20 => light ? new(161, 98, 7) : new(250, 204, 21),
            > 10 => light ? new(194, 65, 12) : new(251, 146, 60),
            _ => light ? new(185, 28, 28) : new(248, 113, 113),
        };

    /// Straight-alpha BGRA pixels, size×size. <paramref name="pct"/> null = no reading yet.
    public static uint[] Render(IconStyle style, DeviceCategory category, int? pct, bool charging, bool lightTaskbar, int size, IconOptions? options = null)
    {
        var opt = options ?? new IconOptions(PercentSign: true); // `new IconOptions()` on a struct is all-zero (no % sign)
        var px = new uint[size * size];
        var fg = Foreground(lightTaskbar);
        var level = pct is int p0 ? LevelColor(p0, charging, lightTaskbar) : Muted(lightTaskbar);
        double s = size;

        switch (style)
        {
            case IconStyle.GlyphBarSide: // symbol, rounded vertical bar at its right, filling from the bottom
            {
                double bw = Math.Max(2.5, s * 0.17), gap = Math.Max(1.5, s * 0.08);
                Paint(px, Symbol(category, opt.IconKey, size, 0, 0, s - bw - gap, s), fg);
                Bar(px, pct, size, s - bw, s * 0.06, bw, s * 0.88, vertical: true, level, lightTaskbar);
                break;
            }
            case IconStyle.GlyphColorSteps: // symbol only, tinted by the level
                Paint(px, Symbol(category, opt.IconKey, size, 0, 0, s, s), level);
                break;
            case IconStyle.GlyphNumber: // symbol on top, pixel-font number centred below
            {
                var text = (pct is int p ? Math.Clamp(p, 0, 100).ToString() : "–") + (opt.PercentSign && pct != null ? "%" : "");
                var font = PixelFont.Fit(text, size, (int)Math.Ceiling(s * 0.36));
                int gap = Math.Max(1, (int)Math.Round(s / 16));
                Paint(px, Symbol(category, opt.IconKey, size, 0, 0, s, s - font.Height - gap), fg);
                font.Draw(px, size, text, (size - font.Width(text)) / 2, size - font.Height,
                    pct is null ? Muted(lightTaskbar) : opt.ColorNumber || charging ? level : fg);
                break;
            }
            default: // IconStyle.Glyph: symbol, rounded horizontal bar below
            {
                double bh = Math.Max(2.5, s * 0.17), gap = Math.Max(1.5, s * 0.08);
                Paint(px, Symbol(category, opt.IconKey, size, 0, 0, s, s - bh - gap), fg);
                Bar(px, pct, size, s * 0.06, s - bh, s * 0.88, bh, vertical: false, level, lightTaskbar);
                break;
            }
        }
        if (opt.Dimmed) // disconnected but still shown: same drawing, faded
            for (int i = 0; i < px.Length; i++) px[i] = px[i] & 0x00FFFFFF | (px[i] >> 24) * 45 / 100 << 24;
        return px;
    }

    /// Rounded track plus rounded fill (both ends round, like a capsule).
    static void Bar(uint[] px, int? pct, int size, double x, double y, double w, double h, bool vertical, Rgb fill, bool light)
    {
        Paint(px, Capsule(size, x, y, w, h), Foreground(light), 0.25);
        if (pct is not int p) return;
        double f = Math.Clamp(p, 0, 100) / 100.0, thick = vertical ? w : h;
        double len = Math.Max(thick, (vertical ? h : w) * f); // never shorter than a dot, so 1 % is visible
        Paint(px, vertical ? Capsule(size, x, y + h - len, w, len) : Capsule(size, x, y, len, h), fill);
    }


    static byte[] Symbol(DeviceCategory category, string? iconKey, int size, double bx, double by, double bw, double bh) =>
        PathCoverage(CategoryIcons.For(category, iconKey), TrayStroke, size, bx, by, bw, bh);

    static uint Bgra(Rgb c, int a) => (uint)a << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    /// Source-over compositing of a coverage layer (straight alpha), with an extra opacity.
    static void Paint(uint[] px, byte[] cov, Rgb c, double opacity = 1)
    {
        for (int i = 0; i < cov.Length; i++)
        {
            double sa = cov[i] / 255.0 * opacity;
            if (sa <= 0) continue;
            uint d = px[i];
            double da = (d >> 24) / 255.0, oa = sa + da * (1 - sa);
            byte Mix(int src, int dst) => (byte)Math.Round((src * sa + dst * da * (1 - sa)) / oa);
            px[i] = Bgra(new Rgb(Mix(c.R, (int)(d >> 16 & 255)), Mix(c.G, (int)(d >> 8 & 255)), Mix(c.B, (int)(d & 255))), (int)Math.Round(oa * 255));
        }
    }

    /// Pixel fonts (3×5 and 5×7) drawn on the pixel grid, scaled by whole pixels only.
    sealed class PixelFont(int glyphW, int glyphH, int scale, Dictionary<char, string[]> glyphs)
    {
        static readonly Dictionary<char, string[]> Small = new()
        {
            ['0'] = ["111", "101", "101", "101", "111"], ['1'] = ["010", "110", "010", "010", "111"], ['2'] = ["111", "001", "111", "100", "111"],
            ['3'] = ["111", "001", "111", "001", "111"], ['4'] = ["101", "101", "111", "001", "001"], ['5'] = ["111", "100", "111", "001", "111"],
            ['6'] = ["111", "100", "111", "101", "111"], ['7'] = ["111", "001", "010", "010", "010"], ['8'] = ["111", "101", "111", "101", "111"],
            ['9'] = ["111", "101", "111", "001", "111"], ['%'] = ["101", "001", "010", "100", "101"], ['–'] = ["000", "000", "111", "000", "000"],
        };
        static readonly Dictionary<char, string[]> Large = new()
        {
            ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"], ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
            ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"], ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
            ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"], ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
            ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"], ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
            ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"], ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
            ['%'] = ["11000", "11001", "00010", "00100", "01000", "10011", "00011"], ['–'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
        };

        public int Height => glyphH * scale;
        public int Width(string text) => (text.Length * (glyphW + 1) - 1) * scale;

        /// The largest font and whole-pixel scale whose text fits maxW × maxH (falls back to 3×5).
        public static PixelFont Fit(string text, int maxW, int maxH)
        {
            foreach (var (w, h, g) in new[] { (5, 7, Large), (3, 5, Small) })
                for (int k = 3; k >= 1; k--)
                {
                    var f = new PixelFont(w, h, k, g);
                    if (f.Width(text) <= maxW && f.Height <= maxH) return f;
                }
            return new PixelFont(3, 5, 1, Small);
        }

        public void Draw(uint[] px, int size, string text, int x, int y, Rgb color)
        {
            for (int i = 0; i < text.Length; i++)
            {
                var rows = glyphs.GetValueOrDefault(text[i]) ?? glyphs['–'];
                for (int ry = 0; ry < glyphH; ry++)
                    for (int rx = 0; rx < glyphW; rx++)
                        if (rows[ry][rx] == '1')
                            for (int dy = 0; dy < scale; dy++)
                                for (int dx = 0; dx < scale; dx++)
                                {
                                    int xx = x + (i * (glyphW + 1) + rx) * scale + dx, yy = y + ry * scale + dy;
                                    if (xx >= 0 && yy >= 0 && xx < size && yy < size) px[yy * size + xx] = Bgra(color, 255);
                                }
            }
        }
    }

    /// Antialiased capsule (rounded rectangle with radius = half the short side), 4×4 supersampled.
    static byte[] Capsule(int size, double x, double y, double w, double h)
    {
        var cov = new byte[size * size];
        double r = Math.Min(w, h) / 2;
        for (int ty = 0; ty < size; ty++)
            for (int tx = 0; tx < size; tx++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double px = tx + (sx + 0.5) / 4, py = ty + (sy + 0.5) / 4;
                        if (px < x || px > x + w || py < y || py > y + h) continue;
                        // Max/Min instead of Clamp: when w == h the bounds can cross by a rounding error
                        double cx = Math.Max(x + r, Math.Min(px, x + w - r)), cy = Math.Max(y + r, Math.Min(py, y + h - r));
                        if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r) hits++;
                    }
                cov[ty * size + tx] = (byte)(hits * 255 / 16);
            }
        return cov;
    }

    /// Coverage map of an SVG stroke icon (24×24 grid) fitted into the box, keeping its aspect ratio.
    static byte[] PathCoverage(string svg, double stroke, int size, double bx, double by, double bw, double bh)
    {
        const double scale = 10; // 24 units -> 240 px, the scratch is 256 px tall
        const int ox = (SrcW - 240) / 2, oy = 8;
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = SrcW, biHeight = -SrcH, biPlanes = 1, biBitCount = 32 };
        nint dc = CreateCompatibleDC(0);
        nint bmp = CreateDIBSection(dc, ref bi, 0, out byte* bits, 0, 0);
        nint oldBmp = SelectObject(dc, bmp);
        var brush = new LOGBRUSH { lbStyle = 0 /* BS_SOLID */, lbColor = 0x00FFFFFF };
        nint pen = ExtCreatePen(PS_GEOMETRIC | PS_SOLID | PS_ENDCAP_ROUND | PS_JOIN_ROUND, (uint)Math.Round(stroke * scale), ref brush, 0, 0);
        nint fill = CreateSolidBrush(0x00FFFFFF);
        nint oldPen = SelectObject(dc, pen), oldBrush = SelectObject(dc, fill);
        static int X(double v) => (int)Math.Round(v * scale) + ox;
        static int Y(double v) => (int)Math.Round(v * scale) + oy;
        try
        {
            var figures = SvgPath.Parse(svg);
            BeginPath(dc);
            foreach (var f in figures.Where(f => !f.IsDot))
            {
                MoveToEx(dc, X(f.Start.X), Y(f.Start.Y), 0);
                var pts = f.Segments.Select(p => new POINT { X = X(p.X), Y = Y(p.Y) }).ToArray();
                fixed (POINT* p = pts) PolyBezierTo(dc, p, (uint)pts.Length);
                if (f.Closed) CloseFigure(dc);
            }
            EndPath(dc);
            StrokePath(dc);

            // Dots (zero-length strokes with round caps) as filled circles
            SelectObject(dc, GetStockObject(NULL_PEN));
            double r = stroke * scale / 2;
            foreach (var f in figures.Where(f => f.IsDot))
                Ellipse(dc, (int)Math.Round(X(f.Start.X) - r), (int)Math.Round(Y(f.Start.Y) - r), (int)Math.Round(X(f.Start.X) + r) + 1, (int)Math.Round(Y(f.Start.Y) + r) + 1);
            GdiFlush();
            return Fit(bits, size, bx, by, bw, bh);
        }
        finally
        {
            SelectObject(dc, oldPen); SelectObject(dc, oldBrush);
            DeleteObject(pen); DeleteObject(fill);
            SelectObject(dc, oldBmp); DeleteObject(bmp); DeleteDC(dc);
        }
    }

    /// Area-resamples the inked region of the scratch into the box of a size×size coverage map,
    /// keeping the aspect ratio and centring it.
    static byte[] Fit(byte* bits, int size, double bx, double by, double bw, double bh)
    {
        int x0 = SrcW, y0 = SrcH, x1 = -1, y1 = -1;
        for (int y = 0; y < SrcH; y++)
            for (int x = 0; x < SrcW; x++)
                if (bits[(y * SrcW + x) * 4 + 2] > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        var cov = new byte[size * size];
        if (x1 < 0) return cov;
        double srcW = x1 - x0 + 1, srcH = y1 - y0 + 1;
        double k = Math.Min(bw / srcW, bh / srcH), dstW = srcW * k, dstH = srcH * k;
        double offX = bx + (bw - dstW) / 2, offY = by + (bh - dstH) / 2;
        double sx = 1 / k, sy = 1 / k;
        for (int ty = 0; ty < size; ty++)
            for (int tx = 0; tx < size; tx++)
            {
                double fx0 = (tx - offX) * sx + x0, fy0 = (ty - offY) * sy + y0;
                int ix0 = Math.Max((int)Math.Floor(fx0), x0), ix1 = Math.Min((int)Math.Ceiling(fx0 + sx), x1 + 1);
                int iy0 = Math.Max((int)Math.Floor(fy0), y0), iy1 = Math.Min((int)Math.Ceiling(fy0 + sy), y1 + 1);
                if (ix1 <= ix0 || iy1 <= iy0) continue;
                // Partial source coverage at the box edges is weighted by the overlap.
                double ox = Math.Min(fx0 + sx, x1 + 1) - Math.Max(fx0, x0), oy = Math.Min(fy0 + sy, y1 + 1) - Math.Max(fy0, y0);
                if (ox <= 0 || oy <= 0) continue;
                long sum = 0;
                for (int y = iy0; y < iy1; y++) for (int x = ix0; x < ix1; x++) sum += bits[(y * SrcW + x) * 4 + 2];
                double avg = sum / (double)((ix1 - ix0) * (iy1 - iy0)) * Math.Min(1, ox * oy / (sx * sy));
                cov[ty * size + tx] = (byte)Math.Min(255, avg * 1.15); // thin strokes read better slightly boosted
            }
        return cov;
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

    // ---- README image (`Voltray.exe --icon-preview file.png`) ----

    public static readonly IconStyle[] Styles = [IconStyle.Glyph, IconStyle.GlyphBarSide, IconStyle.GlyphColorSteps, IconStyle.GlyphNumber];

    /// One block per style; inside, rows = 16/24/32 px × dark/light taskbar, columns = sample devices.
    public static void WritePreview(string path)
    {
        var cases = new (DeviceCategory, int?, bool)[]
        {
            (DeviceCategory.Mouse, 66, false), (DeviceCategory.Keyboard, 94, false), (DeviceCategory.Headphones, 18, false),
            (DeviceCategory.Earbuds, 45, true), (DeviceCategory.Controller, 7, false), (DeviceCategory.Other, null, false),
        };
        int[] sizes = [16, 24, 32];
        const int zoom = 3, cell = 32 * zoom + 16, blockGap = 24;
        int blockW = cases.Length * cell, w = Styles.Length * blockW + (Styles.Length - 1) * blockGap, h = sizes.Length * 2 * cell;
        var sheet = new Rgb[w * h];
        Array.Fill(sheet, new Rgb(250, 250, 252));
        for (int b = 0; b < Styles.Length; b++)
        {
            int row = 0;
            foreach (int size in sizes)
                foreach (bool light in new[] { false, true })
                {
                    Rgb bg = light ? new(243, 243, 243) : new(32, 32, 32); // Windows 11 taskbar
                    for (int c = 0; c < cases.Length; c++)
                    {
                        var (cat, pct, chg) = cases[c];
                        var px = Render(Styles[b], cat, pct, chg, light, size);
                        int left = b * (blockW + blockGap) + c * cell, top = row * cell;
                        for (int y = 0; y < cell; y++)
                            for (int x = 0; x < cell; x++)
                            {
                                int ix = (x - 8) / zoom, iy = (y - 8) / zoom;
                                uint p = x >= 8 && y >= 8 && ix < size && iy < size ? px[iy * size + ix] : 0;
                                int a = (int)(p >> 24);
                                byte Mix(int fg, int bgc) => (byte)((fg * a + bgc * (255 - a)) / 255);
                                sheet[(top + y) * w + left + x] = new(Mix((int)(p >> 16 & 255), bg.R), Mix((int)(p >> 8 & 255), bg.G), Mix((int)(p & 255), bg.B));
                            }
                    }
                    row++;
                }
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
