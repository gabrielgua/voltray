using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Voltray.Settings.Controls;

public enum FillKind { None, Static, Stream, Bubbles, Sparks }

/// A device card's background as its battery level: filled from the left up to the level, with a
/// soft tip. Not charging = a fade-in in the level colour; charging = an animated teal fill.
/// Drawn with Composition (GPU), so the animations cost no UI-thread work.
public sealed partial class LevelFill : Grid
{
    const float Tip = 14; // px of soft fade at the level edge

    static readonly bool AnimationsOn = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    readonly Compositor _c;
    readonly ContainerVisual _root;
    double _fraction;
    FillKind _kind;
    int _step;
    Vector2 _builtSize;
    (double, FillKind, int, bool)? _built;

    public LevelFill()
    {
        IsHitTestVisible = false;
        _c = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _c.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        SizeChanged += (_, _) => Rebuild();
        ActualThemeChanged += (_, _) => Rebuild();
    }

    /// Width of the filled part, in DIPs (for clipping text that sits over it).
    public double FillWidth => Math.Round(ActualWidth * _fraction);
    public event Action? FillChanged;

    /// <param name="step">0 = green, 1 = yellow, 2 = orange, 3 = red (ignored while charging).</param>
    public void Set(double fraction, FillKind kind, int step)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        _kind = kind;
        _step = step;
        Rebuild();
    }

    void Rebuild()
    {
        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        bool dark = ActualTheme == ElementTheme.Dark;
        if (_built == (_fraction, _kind, _step, dark) && _builtSize == size) return;
        _built = (_fraction, _kind, _step, dark);
        _builtSize = size;

        _root.Children.RemoveAll();
        _root.Size = size;
        var clipShape = _c.CreateRoundedRectangleGeometry();
        clipShape.Size = size;
        clipShape.CornerRadius = new Vector2(5, 5);
        _root.Clip = _c.CreateGeometricClip(clipShape);

        float w = MathF.Round(size.X * (float)_fraction), h = size.Y;
        FillChanged?.Invoke();
        if (_kind == FillKind.None || w < 1 || h < 1) return;

        // Draw the fill into its own tree, then show it through a mask that fades the tip.
        var content = _c.CreateContainerVisual();
        content.Size = new Vector2(w, h);
        switch (_kind)
        {
            case FillKind.Static: BuildStatic(content, w, h, dark); break;
            case FillKind.Stream: BuildStream(content, w, h); break;
            case FillKind.Bubbles: BuildBubbles(content, w, h); break;
            case FillKind.Sparks: BuildSparks(content, w, h); break;
        }

        var surface = _c.CreateVisualSurface();
        surface.SourceVisual = content;
        surface.SourceSize = new Vector2(w, h);
        var source = _c.CreateSurfaceBrush(surface);
        source.Stretch = CompositionStretch.None;
        source.HorizontalAlignmentRatio = 0;
        source.VerticalAlignmentRatio = 0;

        var mask = Horizontal();
        float tip = Math.Min(Tip / w, 1f);
        mask.ColorStops.Add(_c.CreateColorGradientStop(0, Colors.White));
        mask.ColorStops.Add(_c.CreateColorGradientStop(1 - tip, Colors.White));
        mask.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));

        var masked = _c.CreateMaskBrush();
        masked.Source = source;
        masked.Mask = mask;
        var sprite = _c.CreateSpriteVisual();
        sprite.Size = new Vector2(w, h);
        sprite.Brush = masked;
        _root.Children.InsertAtTop(sprite);
    }

    // ---- fills ----

    static readonly Color[] LevelColors =
    [
        Color.FromArgb(255, 0x57, 0xD6, 0x8D), // green
        Color.FromArgb(255, 0xFF, 0xD8, 0x4A), // yellow
        Color.FromArgb(255, 0xFF, 0xA2, 0x4D), // orange
        Color.FromArgb(255, 0xFF, 0x7A, 0x85), // red
    ];

    /// Fades in from transparent to the level colour at the tip.
    void BuildStatic(ContainerVisual into, float w, float h, bool dark)
    {
        var c = LevelColors[Math.Clamp(_step, 0, 3)];
        var brush = Horizontal();
        brush.ColorStops.Add(_c.CreateColorGradientStop(0, Color.FromArgb(0, c.R, c.G, c.B)));
        brush.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb((byte)(dark ? 102 : 110), c.R, c.G, c.B)));
        into.Children.InsertAtTop(Sprite(brush, w, h));
    }

    /// Correnteza: teal bands flowing right, and a tip that glows like it is breathing.
    void BuildStream(ContainerVisual into, float w, float h)
    {
        const float period = 170;
        var bands = _c.CreateLinearGradientBrush();
        bands.MappingMode = CompositionMappingMode.Absolute;
        bands.StartPoint = Vector2.Zero;
        bands.EndPoint = new Vector2(period, 0);
        bands.ExtendMode = CompositionGradientExtendMode.Wrap;
        bands.ColorStops.Add(_c.CreateColorGradientStop(0, Hex(0x0F7F78)));
        bands.ColorStops.Add(_c.CreateColorGradientStop(0.5f, Hex(0x1FB5A8)));
        bands.ColorStops.Add(_c.CreateColorGradientStop(1, Hex(0x0F7F78)));
        into.Children.InsertAtTop(Sprite(bands, w, h));
        if (AnimationsOn)
        {
            var flow = _c.CreateVector2KeyFrameAnimation();
            flow.InsertKeyFrame(0, Vector2.Zero);
            flow.InsertKeyFrame(1, new Vector2(period, 0), _c.CreateLinearEasingFunction());
            flow.Duration = TimeSpan.FromSeconds(3.2);
            flow.IterationBehavior = AnimationIterationBehavior.Forever;
            bands.StartAnimation("Offset", flow);
        }

        if (w < 30) return;
        var glowBrush = Horizontal();
        glowBrush.ColorStops.Add(_c.CreateColorGradientStop(0, Color.FromArgb(0, 0xB8, 0xFF, 0xF6)));
        glowBrush.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb(0xCC, 0xB8, 0xFF, 0xF6)));
        var glow = Sprite(glowBrush, 26, h);
        glow.Offset = new Vector3(w - 26, 0, 0);
        into.Children.InsertAtTop(glow);
        if (AnimationsOn)
        {
            var breathe = _c.CreateScalarKeyFrameAnimation();
            breathe.InsertKeyFrame(0, 1);
            breathe.InsertKeyFrame(0.5f, 0.35f);
            breathe.InsertKeyFrame(1, 1);
            breathe.Duration = TimeSpan.FromSeconds(1.6);
            breathe.IterationBehavior = AnimationIterationBehavior.Forever;
            glow.StartAnimation("Opacity", breathe);
        }
    }

    /// Bolhas: teal liquid with bubbles rising and drifting right.
    void BuildBubbles(ContainerVisual into, float w, float h)
    {
        var liquid = _c.CreateLinearGradientBrush();
        liquid.StartPoint = Vector2.Zero;
        liquid.EndPoint = new Vector2(0, 1);
        liquid.ColorStops.Add(_c.CreateColorGradientStop(0, Hex(0x22B3A6)));
        liquid.ColorStops.Add(_c.CreateColorGradientStop(1, Hex(0x0F7F78)));
        into.Children.InsertAtTop(Sprite(liquid, w, h));
        if (!AnimationsOn) return;

        int count = Math.Clamp((int)(w / 14), 2, 14);
        for (int i = 0; i < count; i++)
        {
            float s = 3 + Rand(i, 1) * 6;
            var circle = _c.CreateEllipseGeometry();
            circle.Center = new Vector2(s / 2 + 1, s / 2 + 1);
            circle.Radius = new Vector2(s / 2, s / 2);
            var shape = _c.CreateSpriteShape(circle);
            shape.FillBrush = _c.CreateColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            shape.StrokeBrush = _c.CreateColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            shape.StrokeThickness = 1;
            var bubble = _c.CreateShapeVisual();
            bubble.Size = new Vector2(s + 2, s + 2);
            bubble.Shapes.Add(shape);
            bubble.Opacity = 0;
            into.Children.InsertAtTop(bubble);

            float x = Rand(i, 2) * (w - s);
            var duration = TimeSpan.FromSeconds(2 + Rand(i, 3) * 2.5);
            var delay = TimeSpan.FromSeconds(Rand(i, 4) * 3);
            var rise = _c.CreateVector3KeyFrameAnimation();
            rise.InsertKeyFrame(0, new Vector3(x, h, 0));
            rise.InsertKeyFrame(1, new Vector3(x + 40, h - 78, 0), _c.CreateLinearEasingFunction());
            var fade = _c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(0.15f, 1);
            fade.InsertKeyFrame(0.7f, 1);
            fade.InsertKeyFrame(1, 0);
            foreach (var a in new KeyFrameAnimation[] { rise, fade })
            {
                a.Duration = duration;
                a.DelayTime = delay;
                a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                a.IterationBehavior = AnimationIterationBehavior.Forever;
            }
            bubble.StartAnimation("Offset", rise);
            bubble.StartAnimation("Opacity", fade);
        }
    }

    /// Faíscas: dark teal with streaks of light running right at different speeds.
    void BuildSparks(ContainerVisual into, float w, float h)
    {
        var bg = Horizontal();
        bg.ColorStops.Add(_c.CreateColorGradientStop(0, Hex(0x0B5E5A)));
        bg.ColorStops.Add(_c.CreateColorGradientStop(1, Hex(0x149A90)));
        into.Children.InsertAtTop(Sprite(bg, w, h));
        if (!AnimationsOn) return;

        for (int i = 0; i < 9; i++)
        {
            float len = 18 + Rand(i, 6) * 30;
            var streakBrush = Horizontal();
            streakBrush.ColorStops.Add(_c.CreateColorGradientStop(0, Color.FromArgb(0, 0xC9, 0xFF, 0xF8)));
            streakBrush.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb(0xFF, 0xC9, 0xFF, 0xF8)));
            var streak = Sprite(streakBrush, len, 2);
            streak.Opacity = 0.35f + Rand(i, 7) * 0.6f;
            float y = MathF.Round(h * (0.08f + Rand(i, 5) * 0.8f));
            into.Children.InsertAtTop(streak);

            var run = _c.CreateVector3KeyFrameAnimation();
            run.InsertKeyFrame(0, new Vector3(-len, y, 0));
            run.InsertKeyFrame(1, new Vector3(w + len, y, 0), _c.CreateLinearEasingFunction());
            // Same speed in px/s whatever the fill width, so short fills don't look frantic.
            run.Duration = TimeSpan.FromSeconds((w + 2 * len) / (120 + Rand(i, 8) * 260));
            run.DelayTime = TimeSpan.FromSeconds(Rand(i, 9) * 2);
            run.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            run.IterationBehavior = AnimationIterationBehavior.Forever;
            streak.StartAnimation("Offset", run);
        }
    }

    // ---- helpers ----

    CompositionLinearGradientBrush Horizontal()
    {
        var b = _c.CreateLinearGradientBrush();
        b.StartPoint = Vector2.Zero;
        b.EndPoint = new Vector2(1, 0);
        return b;
    }

    SpriteVisual Sprite(CompositionBrush brush, float w, float h)
    {
        var s = _c.CreateSpriteVisual();
        s.Brush = brush;
        s.Size = new Vector2(w, h);
        return s;
    }

    static Color Hex(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// Stable pseudo-random in [0, 1) so the same card always looks the same.
    static float Rand(int i, int k)
    {
        double v = Math.Sin(i * 12.9898 + k * 78.233) * 43758.5453;
        return (float)(v - Math.Floor(v));
    }
}
