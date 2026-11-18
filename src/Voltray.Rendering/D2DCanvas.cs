using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Voltray.Core.Icons;
using DCommon = Vortice.DCommon;

namespace Voltray.Rendering;

/// A font for <see cref="D2DCanvas"/>: DirectWrite family, size in DIPs, weight.
public sealed record TextStyle(string Family, float Size, FontWeight Weight = FontWeight.Normal);

/// Direct2D + DirectWrite drawing into a GDI memory DC (a 32-bit DIB, premultiplied alpha):
/// the same text and antialiasing engine Windows uses for the taskbar, so small text and icons
/// stay crisp. Coordinates are in DIPs; <see cref="Begin"/> takes the display scale.
public sealed class D2DCanvas : IDisposable
{
    public static readonly ID2D1Factory Factory = D2D1.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.MultiThreaded);
    public static readonly IDWriteFactory Write = DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);

    /// Icon font of the shell (Windows 11), or its Windows 10 predecessor.
    public static readonly string IconFont = Environment.OSVersion.Version.Build >= 22000 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    readonly ID2D1DCRenderTarget _rt;
    readonly ID2D1SolidColorBrush _brush;
    readonly ID2D1StrokeStyle _round;
    readonly Stack<bool> _clips = new(); // true = layer, false = axis-aligned clip

    public D2DCanvas()
    {
        var props = new RenderTargetProperties(RenderTargetType.Default,
            new DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied), 96, 96, RenderTargetUsage.None, FeatureLevel.Default);
        _rt = Factory.CreateDCRenderTarget(props);
        _rt.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale; // ClearType needs an opaque background
        _brush = _rt.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _round = Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, DashCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    public void Begin(nint hdc, int width, int height, float scale)
    {
        _rt.BindDC(hdc, new Vortice.RawRect(0, 0, width, height));
        _rt.Dpi = new Vortice.Mathematics.Size(96 * scale, 96 * scale);
        _rt.BeginDraw();
        _rt.Transform = Matrix3x2.Identity;
        _rt.Clear(new Color4(0, 0, 0, 0));
    }

    public void End()
    {
        while (_clips.Count > 0) PopClip();
        _rt.EndDraw();
    }

    public static Color4 C(uint argb) => new((argb >> 16 & 0xFF) / 255f, (argb >> 8 & 0xFF) / 255f, (argb & 0xFF) / 255f, (argb >> 24) / 255f);

    ID2D1SolidColorBrush Brush(uint argb) { _brush.Color = C(argb); return _brush; }

    public void Fill(float x, float y, float w, float h, uint argb)
    {
        if (w <= 0 || h <= 0) return;
        _rt.FillRectangle(new Rect(x, y, w, h), Brush(argb));
    }

    public void FillRounded(float x, float y, float w, float h, float r, uint argb)
    {
        if (w <= 0 || h <= 0) return;
        _rt.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(x, y, w, h), r, r), Brush(argb));
    }

    /// Vertical gradient from <paramref name="top"/> to <paramref name="bottom"/> colour.
    public void VerticalGradient(float x, float y, float w, float h, uint top, uint bottom)
    {
        if (w <= 0 || h <= 0) return;
        using var stops = _rt.CreateGradientStopCollection([new GradientStop(0, C(top)), new GradientStop(1, C(bottom))], ExtendMode.Clamp);
        using var brush = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, y), new Vector2(0, y + h)), stops);
        _rt.FillRectangle(new Rect(x, y, w, h), brush);
    }

    /// Repeating vertical bands: <paramref name="colors"/> spread over <paramref name="period"/>, starting at <paramref name="phase"/>.
    public void Bands(float x, float y, float w, float h, uint[] colors, float phase, float period)
    {
        if (w <= 0 || h <= 0) return;
        var stops = colors.Select((c, i) => new GradientStop(i / (float)(colors.Length - 1), C(c))).ToArray();
        using var coll = _rt.CreateGradientStopCollection(stops, ExtendMode.Wrap);
        using var brush = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, phase), new Vector2(0, phase + period)), coll);
        _rt.FillRectangle(new Rect(x, y, w, h), brush);
    }

    /// Clips what follows to a rounded rectangle (antialiased edges), until <see cref="PopClip"/>.
    public void PushRoundedClip(float x, float y, float w, float h, float r)
    {
        using var geo = Factory.CreateRoundedRectangleGeometry(new RoundedRectangle(new System.Drawing.RectangleF(x, y, w, h), r, r));
        _rt.PushLayer(new LayerParameters
        {
            ContentBounds = new Rect(float.MinValue / 2, float.MinValue / 2, float.MaxValue, float.MaxValue),
            GeometricMask = geo, MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = Matrix3x2.Identity, Opacity = 1,
        }, null);
        _clips.Push(true);
    }

    public void PushClip(float x, float y, float w, float h)
    {
        _rt.PushAxisAlignedClip(new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), AntialiasMode.Aliased);
        _clips.Push(false);
    }

    public void PopClip()
    {
        if (_clips.Pop()) _rt.PopLayer(); else _rt.PopAxisAlignedClip();
    }

    // ---- icons

    sealed record IconPath(ID2D1PathGeometry Path, List<PointD> Dots);
    static readonly Dictionary<string, IconPath> Icons = [];

    /// Hugeicons stroke icon (24×24 grid) fitted into a size×size box.
    public void Icon(string svg, float x, float y, float size, uint color, float stroke = (float)CategoryIcons.Stroke)
    {
        IconPath icon;
        lock (Icons)
            if (!Icons.TryGetValue(svg, out icon!))
            {
                var path = Factory.CreatePathGeometry();
                var dots = new List<PointD>();
                using (var sink = path.Open())
                {
                    foreach (var f in SvgPath.Parse(svg))
                    {
                        if (f.IsDot) { dots.Add(f.Start); continue; }
                        sink.BeginFigure(new Vector2((float)f.Start.X, (float)f.Start.Y), FigureBegin.Hollow);
                        for (int i = 0; i + 2 < f.Segments.Count; i += 3)
                            sink.AddBezier(new BezierSegment(V(f.Segments[i]), V(f.Segments[i + 1]), V(f.Segments[i + 2])));
                        sink.EndFigure(f.Closed ? FigureEnd.Closed : FigureEnd.Open);
                    }
                    sink.Close();
                }
                Icons[svg] = icon = new IconPath(path, dots);
            }
        var saved = _rt.Transform;
        _rt.Transform = Matrix3x2.CreateScale(size / 24) * Matrix3x2.CreateTranslation(x, y) * saved;
        var brush = Brush(color);
        _rt.DrawGeometry(icon.Path, brush, stroke, _round);
        foreach (var d in icon.Dots) _rt.FillEllipse(new Ellipse(V(d), stroke / 2, stroke / 2), brush);
        _rt.Transform = saved;
    }

    static Vector2 V(PointD p) => new((float)p.X, (float)p.Y);

    // ---- text

    static readonly Dictionary<(TextStyle, TextAlignment), IDWriteTextFormat> Formats = [];

    static IDWriteTextFormat Format(TextStyle s, TextAlignment align)
    {
        lock (Formats)
        {
            if (Formats.TryGetValue((s, align), out var f)) return f;
            f = Write.CreateTextFormat(s.Family, s.Weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, s.Size);
            f.WordWrapping = WordWrapping.NoWrap;
            f.TextAlignment = align;
            using var sign = Write.CreateEllipsisTrimmingSign(f);
            f.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, sign);
            return Formats[(s, align)] = f;
        }
    }

    /// Single line of text whose line box starts at (x, top), trimmed with "…" past <paramref name="maxWidth"/>.
    public void Text(string text, TextStyle style, float x, float top, float maxWidth, uint argb, TextAlignment align = TextAlignment.Leading)
    {
        if (text.Length == 0) return;
        using var layout = Write.CreateTextLayout(text, Format(style, align), Math.Max(1, maxWidth), style.Size * 2);
        _rt.DrawTextLayout(new Vector2(x, top), layout, Brush(argb), DrawTextOptions.None);
    }

    /// Text whose baseline sits at <paramref name="baseline"/>.
    public void TextOnBaseline(string text, TextStyle style, float x, float baseline, uint argb)
    {
        if (text.Length == 0) return;
        using var layout = Write.CreateTextLayout(text, Format(style, TextAlignment.Leading), 1000, style.Size * 2);
        var lines = new LineMetrics[1];
        layout.GetLineMetrics(lines, out _);
        float ascent = lines[0].Baseline > 0 ? lines[0].Baseline : style.Size;
        _rt.DrawTextLayout(new Vector2(x, baseline - ascent), layout, Brush(argb), DrawTextOptions.None);
    }

    public static float Measure(string text, TextStyle style)
    {
        if (text.Length == 0) return 0;
        using var layout = Write.CreateTextLayout(text, Format(style, TextAlignment.Leading), 1000, style.Size * 2);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }

    public void Dispose()
    {
        _round.Dispose();
        _brush.Dispose();
        _rt.Dispose();
    }
}
