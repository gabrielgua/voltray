using System.Globalization;
using System.Text;
using Voltray.Core.Icons;
using static Voltray.Rendering.Gdi;

namespace Voltray.Rendering;

/// Turns a stroked Hugeicons path into a filled outline (XAML path markup), so UI that only fills
/// geometry (WinUI PathIcon) can draw the icon as a crisp vector in the current text colour.
public static unsafe class IconOutline
{
    const double Scale = 64; // GDI paths use integer coordinates: work at 64 units per SVG unit

    /// Path markup ("F1 M … Z") for <paramref name="svg"/> drawn with <paramref name="stroke"/> at size×size px.
    public static string Markup(string svg, double size, double stroke = CategoryIcons.Stroke)
    {
        var figures = SvgPath.Parse(svg);
        double k = size / 24 / Scale;
        var sb = new StringBuilder("F1");
        void Pt(char cmd, double x, double y) =>
            sb.Append(' ').Append(cmd).Append(' ').Append((x * k).ToString("0.###", CultureInfo.InvariantCulture))
              .Append(',').Append((y * k).ToString("0.###", CultureInfo.InvariantCulture));
        static int I(double v) => (int)Math.Round(v * Scale);

        nint dc = CreateCompatibleDC(0);
        var brush = new LOGBRUSH { lbStyle = 0, lbColor = 0 };
        nint pen = ExtCreatePen(PS_GEOMETRIC | PS_SOLID | PS_ENDCAP_ROUND | PS_JOIN_ROUND, (uint)Math.Round(stroke * Scale), ref brush, 0, 0);
        nint oldPen = SelectObject(dc, pen);
        try
        {
            BeginPath(dc);
            foreach (var f in figures.Where(f => !f.IsDot))
            {
                MoveToEx(dc, I(f.Start.X), I(f.Start.Y), 0);
                var pts = f.Segments.Select(p => new POINT { X = I(p.X), Y = I(p.Y) }).ToArray();
                fixed (POINT* p = pts) PolyBezierTo(dc, p, (uint)pts.Length);
                if (f.Closed) CloseFigure(dc);
            }
            EndPath(dc);
            WidenPath(dc);
            int n = GetPath(dc, null, null, 0);
            if (n > 0)
            {
                var pts = new POINT[n];
                var types = new byte[n];
                fixed (POINT* p = pts) fixed (byte* t = types) n = GetPath(dc, p, t, n);
                for (int i = 0; i < n; i++)
                {
                    int type = types[i] & ~PT_CLOSEFIGURE;
                    if (type == PT_MOVETO) Pt('M', pts[i].X, pts[i].Y);
                    else if (type == PT_BEZIERTO && i + 2 < n)
                    {
                        Pt('C', pts[i].X, pts[i].Y); Pt(' ', pts[i + 1].X, pts[i + 1].Y); Pt(' ', pts[i + 2].X, pts[i + 2].Y);
                        i += 2;
                    }
                    else Pt('L', pts[i].X, pts[i].Y);
                    if ((types[i] & PT_CLOSEFIGURE) != 0) sb.Append(" Z");
                }
            }
        }
        finally
        {
            SelectObject(dc, oldPen); DeleteObject(pen); DeleteDC(dc);
        }

        // Dots (zero-length strokes with round caps) as filled circles
        double r = stroke * Scale / 2;
        foreach (var f in figures.Where(f => f.IsDot))
        {
            double cx = f.Start.X * Scale, cy = f.Start.Y * Scale;
            string R = (r * k).ToString("0.###", CultureInfo.InvariantCulture);
            Pt('M', cx - r, cy);
            sb.Append($" A {R},{R} 0 1 0"); Pt(' ', cx + r, cy);
            sb.Append($" A {R},{R} 0 1 0"); Pt(' ', cx - r, cy);
            sb.Append(" Z");
        }
        return sb.ToString();
    }
}
