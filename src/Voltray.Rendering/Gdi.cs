using System.Runtime.InteropServices;

namespace Voltray.Rendering;

/// The few GDI calls the renderer needs: a scratch DIB, path stroking, and icon creation.
internal static unsafe partial class Gdi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public int fIcon, xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] public struct LOGBRUSH { public uint lbStyle, lbColor; public nint lbHatch; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public const uint PS_GEOMETRIC = 0x10000, PS_SOLID = 0, PS_ENDCAP_ROUND = 0, PS_JOIN_ROUND = 0;
    public const int NULL_PEN = 8;

    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bi, uint usage, out byte* bits, nint sec, uint off);
    [LibraryImport("gdi32.dll")] public static partial nint CreateBitmap(int w, int h, uint planes, uint bpp, nint bits);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GdiFlush();
    [LibraryImport("gdi32.dll")] public static partial nint ExtCreatePen(uint style, uint width, ref LOGBRUSH brush, uint count, nint styles);
    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")] public static partial nint GetStockObject(int obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BeginPath(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool EndPath(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseFigure(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool StrokePath(nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool MoveToEx(nint dc, int x, int y, nint prev);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PolyBezierTo(nint dc, POINT* pts, uint count);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool Ellipse(nint dc, int l, int t, int r, int b);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool WidenPath(nint dc);
    [LibraryImport("gdi32.dll")] public static partial int GetPath(nint dc, POINT* pts, byte* types, int count);
    public const int PT_CLOSEFIGURE = 1, PT_LINETO = 2, PT_BEZIERTO = 4, PT_MOVETO = 6;
    [LibraryImport("user32.dll")] public static partial nint CreateIconIndirect(ref ICONINFO ii);
}
