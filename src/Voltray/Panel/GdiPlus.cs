using System.Runtime.InteropServices;

namespace Voltray.Panel;

/// The slice of the GDI+ flat API the tray panel draws with: antialiased paths, gradients and
/// text on a premultiplied 32-bit bitmap. No COM, so it stays NativeAOT-friendly.
internal static unsafe partial class GdiPlus
{
    public const int PixelFormat32bppPARGB = 0x000E200B;
    public const int SmoothingModeAntiAlias = 4, PixelOffsetModeHalf = 4, TextRenderingHintAntiAliasGridFit = 3;
    public const int UnitPixel = 2, UnitWorld = 0, FontStyleRegular = 0, FontStyleBold = 1;
    public const int WrapModeTile = 0, WrapModeClamp = 4, LineCapRound = 2, LineJoinRound = 2;
    public const int StringFormatFlagsNoWrap = 0x1000, StringTrimmingEllipsisCharacter = 3;
    public const int StringAlignmentNear = 0, StringAlignmentCenter = 1, StringAlignmentFar = 2;

    [StructLayout(LayoutKind.Sequential)] public struct StartupInput { public uint GdiplusVersion; public nint DebugEventCallback; public int SuppressBackgroundThread, SuppressExternalCodecs; }
    [StructLayout(LayoutKind.Sequential)] public struct PointF(float x, float y) { public float X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] public struct RectF(float x, float y, float w, float h) { public float X = x, Y = y, Width = w, Height = h; }

    const string Dll = "gdiplus.dll";
    [LibraryImport(Dll)] public static partial int GdiplusStartup(out nint token, in StartupInput input, nint output);
    [LibraryImport(Dll)] public static partial int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, byte* scan0, out nint bitmap);
    [LibraryImport(Dll)] public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [LibraryImport(Dll)] public static partial int GdipDeleteGraphics(nint g);
    [LibraryImport(Dll)] public static partial int GdipDisposeImage(nint image);
    [LibraryImport(Dll)] public static partial int GdipSetSmoothingMode(nint g, int mode);
    [LibraryImport(Dll)] public static partial int GdipSetPixelOffsetMode(nint g, int mode);
    [LibraryImport(Dll)] public static partial int GdipSetTextRenderingHint(nint g, int hint);
    [LibraryImport(Dll)] public static partial int GdipGraphicsClear(nint g, uint argb);
    [LibraryImport(Dll)] public static partial int GdipScaleWorldTransform(nint g, float sx, float sy, int order);
    [LibraryImport(Dll)] public static partial int GdipTranslateWorldTransform(nint g, float dx, float dy, int order);
    [LibraryImport(Dll)] public static partial int GdipSaveGraphics(nint g, out uint state);
    [LibraryImport(Dll)] public static partial int GdipRestoreGraphics(nint g, uint state);
    [LibraryImport(Dll)] public static partial int GdipSetClipPath(nint g, nint path, int combine);
    [LibraryImport(Dll)] public static partial int GdipResetClip(nint g);
    [LibraryImport(Dll)] public static partial int GdipSetClipRect(nint g, float x, float y, float w, float h, int combine);

    [LibraryImport(Dll)] public static partial int GdipCreatePath(int fillMode, out nint path);
    [LibraryImport(Dll)] public static partial int GdipDeletePath(nint path);
    [LibraryImport(Dll)] public static partial int GdipStartPathFigure(nint path);
    [LibraryImport(Dll)] public static partial int GdipClosePathFigure(nint path);
    [LibraryImport(Dll)] public static partial int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Dll)] public static partial int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
    [LibraryImport(Dll)] public static partial int GdipAddPathEllipse(nint path, float x, float y, float w, float h);

    [LibraryImport(Dll)] public static partial int GdipCreateSolidFill(uint argb, out nint brush);
    [LibraryImport(Dll)] public static partial int GdipCreateLineBrush(in PointF p1, in PointF p2, uint c1, uint c2, int wrap, out nint brush);
    [LibraryImport(Dll)] public static partial int GdipSetLinePresetBlend(nint brush, uint* colors, float* positions, int count);
    [LibraryImport(Dll)] public static partial int GdipDeleteBrush(nint brush);
    [LibraryImport(Dll)] public static partial int GdipFillPath(nint g, nint brush, nint path);
    [LibraryImport(Dll)] public static partial int GdipFillRectangle(nint g, nint brush, float x, float y, float w, float h);

    [LibraryImport(Dll)] public static partial int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [LibraryImport(Dll)] public static partial int GdipSetPenStartCap(nint pen, int cap);
    [LibraryImport(Dll)] public static partial int GdipSetPenEndCap(nint pen, int cap);
    [LibraryImport(Dll)] public static partial int GdipSetPenLineJoin(nint pen, int join);
    [LibraryImport(Dll)] public static partial int GdipDeletePen(nint pen);
    [LibraryImport(Dll)] public static partial int GdipDrawPath(nint g, nint pen, nint path);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)] public static partial int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);
    [LibraryImport(Dll)] public static partial int GdipDeleteFontFamily(nint family);
    [LibraryImport(Dll)] public static partial int GdipGetCellAscent(nint family, int style, out ushort ascent);
    [LibraryImport(Dll)] public static partial int GdipGetEmHeight(nint family, int style, out ushort em);
    [LibraryImport(Dll)] public static partial int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
    [LibraryImport(Dll)] public static partial int GdipDeleteFont(nint font);
    [LibraryImport(Dll)] public static partial int GdipStringFormatGetGenericTypographic(out nint format);
    [LibraryImport(Dll)] public static partial int GdipCloneStringFormat(nint format, out nint clone);
    [LibraryImport(Dll)] public static partial int GdipGetStringFormatFlags(nint format, out int flags);
    [LibraryImport(Dll)] public static partial int GdipSetStringFormatFlags(nint format, int flags);
    [LibraryImport(Dll)] public static partial int GdipSetStringFormatTrimming(nint format, int trimming);
    [LibraryImport(Dll)] public static partial int GdipSetStringFormatAlign(nint format, int align);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)] public static partial int GdipDrawString(nint g, string text, int length, nint font, in RectF layout, nint format, nint brush);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)] public static partial int GdipMeasureString(nint g, string text, int length, nint font, in RectF layout, nint format, out RectF bounds, out int codepoints, out int lines);

    static nint _token;

    public static void EnsureStarted()
    {
        if (_token != 0) return;
        var input = new StartupInput { GdiplusVersion = 1 };
        int status = GdiplusStartup(out _token, input, 0);
        if (status != 0) throw new InvalidOperationException($"GdiplusStartup failed ({status})");
    }
}
