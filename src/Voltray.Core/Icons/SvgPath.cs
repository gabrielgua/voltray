using System.Globalization;

namespace Voltray.Core.Icons;

/// A figure of an SVG path flattened to cubic Béziers: <see cref="Start"/>, then segments of
/// three points each (control 1, control 2, end). Lines are Béziers with controls on the line.
public sealed record Figure(PointD Start, IReadOnlyList<PointD> Segments, bool Closed)
{
    /// A dot: a figure that does not move (Hugeicons draws dots as zero-length strokes with round caps).
    public bool IsDot => Segments.All(p => Math.Abs(p.X - Start.X) < 0.05 && Math.Abs(p.Y - Start.Y) < 0.05);
}

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD a, double k) => new(a.X * k, a.Y * k);
}

/// Minimal SVG path parser (M L H V C S Q A Z, absolute and relative) producing cubic Béziers.
/// Enough for the stroke icons in this project; renderers without arcs (GDI) can draw the result.
public static class SvgPath
{
    public static List<Figure> Parse(string d)
    {
        var figures = new List<Figure>();
        var tokens = Tokenize(d);
        int i = 0;
        char cmd = 'M';
        PointD cur = default, start = default, lastCtrl = default;
        List<PointD>? segs = null;

        void Flush(bool closed)
        {
            if (segs != null) figures.Add(new Figure(start, segs, closed));
            segs = null;
        }
        void Line(PointD to) { (segs ??= []).AddRange([cur + (to - cur) * (1 / 3.0), cur + (to - cur) * (2 / 3.0), to]); lastCtrl = to; cur = to; }
        void Cubic(PointD c1, PointD c2, PointD to) { (segs ??= []).AddRange([c1, c2, to]); lastCtrl = c2; cur = to; }
        double Num() => double.Parse(tokens[i++], CultureInfo.InvariantCulture);
        bool HasNum() => i < tokens.Count && !char.IsLetter(tokens[i][0]);

        while (i < tokens.Count)
        {
            if (char.IsLetter(tokens[i][0])) cmd = tokens[i++][0];
            bool rel = char.IsLower(cmd);
            PointD P(double x, double y) => rel ? cur + new PointD(x, y) : new PointD(x, y);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    Flush(false);
                    cur = start = P(Num(), Num());
                    segs = [];
                    cmd = rel ? 'l' : 'L'; // following pairs are implicit line-tos
                    break;
                case 'L': Line(P(Num(), Num())); break;
                case 'H': { double x = Num(); Line(new PointD(rel ? cur.X + x : x, cur.Y)); break; }
                case 'V': { double y = Num(); Line(new PointD(cur.X, rel ? cur.Y + y : y)); break; }
                case 'C': { var c1 = P(Num(), Num()); var c2 = P(Num(), Num()); Cubic(c1, c2, P(Num(), Num())); break; }
                case 'S': { var c1 = cur + (cur - lastCtrl); var c2 = P(Num(), Num()); Cubic(c1, c2, P(Num(), Num())); break; }
                case 'Q':
                {
                    var q = P(Num(), Num()); var to = P(Num(), Num());
                    Cubic(cur + (q - cur) * (2 / 3.0), to + (q - to) * (2 / 3.0), to);
                    break;
                }
                case 'A':
                {
                    double rx = Num(), ry = Num(), rot = Num(); bool large = Num() != 0, sweep = Num() != 0;
                    Arc(cur, rx, ry, rot, large, sweep, P(Num(), Num()), Cubic);
                    break;
                }
                case 'Z':
                    if (segs != null && (cur.X != start.X || cur.Y != start.Y)) Line(start);
                    Flush(true);
                    cur = start;
                    break;
                default: throw new FormatException($"Unsupported SVG path command '{cmd}'");
            }
            if (char.ToUpperInvariant(cmd) == 'Z' && HasNum()) throw new FormatException("Numbers after Z");
        }
        Flush(false);
        return figures;
    }

    static List<string> Tokenize(string d)
    {
        var t = new List<string>();
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c) && c != 'e' && c != 'E') { t.Add(c.ToString()); i++; continue; }
            int s = i;
            if (c is '-' or '+') i++;
            bool dot = false, exp = false;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsDigit(ch)) i++;
                else if (ch == '.' && !dot && !exp) { dot = true; i++; }
                else if ((ch is 'e' or 'E') && !exp) { exp = true; i++; if (i < d.Length && d[i] is '-' or '+') i++; }
                else break;
            }
            t.Add(d[s..i]);
        }
        return t;
    }

    /// SVG elliptical arc (endpoint form) as cubic Béziers, ≤ 90° each (SVG spec, appendix F.6).
    static void Arc(PointD p0, double rx, double ry, double rotDeg, bool large, bool sweep, PointD p1, Action<PointD, PointD, PointD> cubic)
    {
        if (rx == 0 || ry == 0) { cubic(p0, p1, p1); return; }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        double phi = rotDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (p0.X - p1.X) / 2, dy = (p0.Y - p1.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1) { rx *= Math.Sqrt(lambda); ry *= Math.Sqrt(lambda); }
        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
        double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (p0.X + p1.X) / 2, cy = sin * cx1 + cos * cy1 + (p0.Y + p1.Y) / 2;
        double Angle(double ux, double uy, double vx, double vy) =>
            Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        double t1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double dt = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && dt > 0) dt -= 2 * Math.PI;
        if (sweep && dt < 0) dt += 2 * Math.PI;

        int n = (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2));
        double step = dt / n, k = 4.0 / 3 * Math.Tan(step / 4);
        PointD At(double t) => new(cx + rx * Math.Cos(t) * cos - ry * Math.Sin(t) * sin, cy + rx * Math.Cos(t) * sin + ry * Math.Sin(t) * cos);
        PointD D(double t) => new(-rx * Math.Sin(t) * cos - ry * Math.Cos(t) * sin, -rx * Math.Sin(t) * sin + ry * Math.Cos(t) * cos);
        double a = t1;
        for (int s = 0; s < n; s++)
        {
            double b = a + step;
            cubic(At(a) + D(a) * k, At(b) - D(b) * k, s == n - 1 ? p1 : At(b));
            a = b;
        }
    }
}
