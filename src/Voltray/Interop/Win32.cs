using System.Runtime.InteropServices;

namespace Voltray.Interop;

internal static unsafe partial class Win32
{
    public const uint WM_DESTROY = 0x0002, WM_COMMAND = 0x0111, WM_TIMER = 0x0113, WM_SETTINGCHANGE = 0x001A,
        WM_DPICHANGED = 0x02E0, WM_CONTEXTMENU = 0x007B, WM_LBUTTONUP = 0x0202, WM_APP = 0x8000,
        NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_GUID = 0x20, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x1, NIIF_WARNING = 0x2, NIIF_ERROR = 0x3, NIIF_RESPECT_QUIET_TIME = 0x80;
    public const uint NIN_BALLOONUSERCLICK = 0x0405;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4, NOTIFYICON_VERSION_4 = 4;
    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    public const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style; public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground; public char* lpszMenuName, lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATAW data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] public static partial ushort RegisterClassEx(ref WNDCLASSEXW wc);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] public static partial nint DefWindowProc(nint h, uint m, nint w, nint l);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] public static partial int GetMessage(out MSG m, nint h, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(in MSG m);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] public static partial nint DispatchMessage(in MSG m);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool PostMessage(nint h, uint m, nint w, nint l);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessage(string s);
    [LibraryImport("user32.dll")] public static partial nint SetTimer(nint h, nint id, uint ms, nint fn);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool KillTimer(nint h, nint id);
    public const uint WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007;
    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool AppendMenu(nint m, uint f, nint id, string? text);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenu(nint m, uint f, int x, int y, int r, nint h, nint rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyMenu(nint m);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(nint h);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindow(string? cls, string? title);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool ShowWindow(nint h, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsIconic(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetMenuDefaultItem(nint m, uint item, uint byPos);
    public const int SW_RESTORE = 9;
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT p);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll")] public static partial int GetSystemMetricsForDpi(int idx, uint dpi);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(nint h);

}
