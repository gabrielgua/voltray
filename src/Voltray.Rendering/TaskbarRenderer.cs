using Vortice.DirectWrite;
using Voltray.Ipc;
using static Voltray.Rendering.Gdi;

namespace Voltray.Rendering;

/// One device as drawn on the taskbar.
/// <param name="Percent">null when the device reports no percentage (then <paramref name="Word"/> is shown).</param>
public readonly record struct TaskbarDevice(string IconPath, int? Percent, bool Charging, string? Word = null);

/// A rendered strip: premultiplied BGRA pixels, top-down, ready for UpdateLayeredWindow or a WriteableBitmap.
public sealed record TaskbarFrame(int Width, int Height, byte[] Pixels);

/// Draws the devices that sit on the taskbar next to the tray, in the three taskbar styles, with
/// Direct2D/DirectWrite like the taskbar's own text. Used by the tray host (the real thing) and
/// by the settings window (previews).
public static unsafe class TaskbarRenderer
{
    const float PadX = 4, ItemGap = 10;

    static readonly TextStyle Number = new("Segoe UI Variable Text", 13), Small = new("Segoe UI Variable Text", 11);

    [ThreadStatic] static D2DCanvas? _canvas;

    public static TaskbarFrame Render(IReadOnlyList<TaskbarDevice> devices, TaskbarStyle style, bool light, float scale, int heightPx, bool hover = false)
    {
        float h = heightPx / scale;
        var layout = Layout(devices, style);
        int w = Math.Max(1, (int)MathF.Ceiling((layout.Width + 2 * PadX) * scale));

        nint dc = CreateCompatibleDC(0);
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -heightPx, biPlanes = 1, biBitCount = 32 };
        nint dib = CreateDIBSection(dc, ref bi, 0, out byte* bits, 0, 0);
        nint old = SelectObject(dc, dib);
        try
        {
            var canvas = _canvas ??= new D2DCanvas();
            canvas.Begin(dc, w, heightPx, scale);
            uint ink = light ? 0xFF1A1A1A : 0xFFFFFFFF;
            if (hover) canvas.FillRounded(0, 4, w / scale, h - 8, 4, WithAlpha(ink, 0x16));
            Draw(canvas, devices, style, layout, light, h);
            canvas.End();
            return new TaskbarFrame(w, heightPx, new ReadOnlySpan<byte>(bits, w * heightPx * 4).ToArray());
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    static uint WithAlpha(uint argb, int alpha) => (uint)Math.Clamp(alpha, 0, 255) << 24 | (argb & 0xFFFFFF);

    // ---- layout (DIPs)

    sealed record Cell(TaskbarDevice Device, string Text, float X, int Row, float TextWidth);
    sealed record LayoutResult(float Width, List<Cell> Cells, float IconSize, TextStyle Font);

    static LayoutResult Layout(IReadOnlyList<TaskbarDevice> devices, TaskbarStyle style)
    {
        bool compact = style == TaskbarStyle.Compact;
        var font = compact ? Small : Number;
        float icon = compact ? 13 : 18, gap = compact ? 4 : 5;
        var texts = devices.Select(Label).ToList();
        var widths = texts.Select(t => D2DCanvas.Measure(t, font)).ToList();
        var cells = new List<Cell>();
        if (devices.Count == 0) return new(0, cells, icon, font);

        if (!compact)
        {
            float x = PadX;
            for (int i = 0; i < devices.Count; i++)
            {
                float tw = style == TaskbarStyle.IconNumberBar ? Math.Max(widths[i], 24) : widths[i];
                cells.Add(new Cell(devices[i], texts[i], x, 0, tw));
                x += icon + gap + tw + ItemGap;
            }
            return new(x - ItemGap - PadX, cells, icon, font);
        }

        // Compact: two rows filled left to right (one row, centred, when there is a single device)
        int rows = devices.Count >= 2 ? 2 : 1, cols = (devices.Count + rows - 1) / rows;
        float cx = PadX;
        for (int c = 0; c < cols; c++)
        {
            float colW = 0;
            for (int r = 0; r < rows; r++)
            {
                int i = r * cols + c;
                if (i >= devices.Count) continue;
                colW = Math.Max(colW, icon + gap + widths[i]);
                cells.Add(new Cell(devices[i], texts[i], cx, r, widths[i]));
            }
            cx += colW + ItemGap;
        }
        return new(cx - ItemGap - PadX, cells, icon, font);
    }

    static string Label(TaskbarDevice d) => d.Percent is int p ? $"{p}%" : d.Word ?? "—";

    // ---- drawing

    static void Draw(D2DCanvas g, IReadOnlyList<TaskbarDevice> devices, TaskbarStyle style, LayoutResult l, bool light, float h)
    {
        uint ink = light ? 0xFF1A1A1A : 0xFFFFFFFF;
        uint charge = light ? 0xFF007A74 : 0xFF6FF0E0, low = light ? 0xFFC42B1C : 0xFFFF99A4;
        float mid = h / 2;
        bool compact = style == TaskbarStyle.Compact;
        int rows = compact && devices.Count >= 2 ? 2 : 1;
        const float rowH = 16, rowGap = 1;
        float gap = compact ? 4 : 5;

        foreach (var cell in l.Cells)
        {
            var d = cell.Device;
            uint color = d.Charging ? charge : d.Percent is <= 20 ? low : ink;
            float cy = compact ? mid - (rows * rowH + (rows - 1) * rowGap) / 2 + cell.Row * (rowH + rowGap) + rowH / 2 : mid;
            float tx = cell.X + l.IconSize + gap;
            g.Icon(d.IconPath, cell.X, cy - l.IconSize / 2, l.IconSize, ink, compact ? 1.9f : 1.6f);

            if (style == TaskbarStyle.IconNumberBar)
            {
                const float barH = 3, barGap = 3;
                float capH = l.Font.Size * 0.7f; // digits are about 0.7 em tall
                float top = mid - (capH + barGap + barH) / 2;
                g.TextOnBaseline(cell.Text, l.Font, tx, top + capH, color);
                float barY = top + capH + barGap;
                g.FillRounded(tx, barY, cell.TextWidth, barH, barH / 2, WithAlpha(ink, 0x33));
                float fill = cell.TextWidth * Math.Clamp((d.Percent ?? 0) / 100f, 0, 1);
                if (fill > 0) g.FillRounded(tx, barY, Math.Max(fill, barH), barH, barH / 2, LevelColor(d, light));
            }
            else g.TextOnBaseline(cell.Text, l.Font, tx, cy + l.Font.Size * 0.35f, color); // digits centred on their cap height
        }
    }

    static uint LevelColor(TaskbarDevice d, bool light) =>
        d.Charging ? (light ? 0xFF007A74 : 0xFF6FF0E0)
        : d.Percent is <= 20 ? (light ? 0xFFC42B1C : 0xFFFF99A4)
        : d.Percent is <= 50 ? 0xFFE0B000
        : 0xFF4CC27A;
}
