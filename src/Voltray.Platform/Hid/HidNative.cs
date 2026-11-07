using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Voltray.Platform.Hid;

/// Minimal Win32 HID / SetupAPI interop (AOT-friendly, no reflection).
public static unsafe partial class HidNative
{
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public nint Reserved; }

    [LibraryImport("hid.dll")] private static partial void HidD_GetHidGuid(out Guid guid);
    [LibraryImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [LibraryImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool HidD_GetPreparsedData(SafeFileHandle h, out nint data);
    [LibraryImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool HidD_FreePreparsedData(nint data);
    [LibraryImport("hid.dll")] public static partial int HidP_GetCaps(nint data, out HIDP_CAPS caps);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint sa, uint disp, uint flags, nint template);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial nint SetupDiGetClassDevsW(ref Guid g, nint enumerator, nint hwnd, uint flags);
    [LibraryImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint set, nint devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [LibraryImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SP_DEVICE_INTERFACE_DATA data, byte* detail, int size, out int required, nint devInfo);
    [LibraryImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    /// <param name="Error">Win32 error when the collection could not be queried (then usages are 0).</param>
    public sealed record HidCollection(string Path, ushort Vid, ushort Pid, ushort UsagePage, ushort Usage, int InputLen, int OutputLen, int Error);

    /// Every present HID collection whose path matches the vendor id, with its top-level usage.
    /// Opens with zero access rights, which works even for collections owned exclusively by the OS.
    public static List<HidCollection> EnumerateCollections(ushort vid)
    {
        var result = new List<HidCollection>();
        string tag = $"vid_{vid:x4}";
        foreach (var path in EnumerateHidPaths())
        {
            if (!path.Contains(tag, StringComparison.OrdinalIgnoreCase)) continue;
            using var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
            if (h.IsInvalid) { result.Add(new(path, vid, 0, 0, 0, 0, 0, Marshal.GetLastPInvokeError())); continue; }
            var attr = new HIDD_ATTRIBUTES { Size = sizeof(HIDD_ATTRIBUTES) };
            if (!HidD_GetAttributes(h, ref attr) || !HidD_GetPreparsedData(h, out var pp))
            { result.Add(new(path, vid, 0, 0, 0, 0, 0, Marshal.GetLastPInvokeError())); continue; }
            HidP_GetCaps(pp, out var caps);
            HidD_FreePreparsedData(pp);
            result.Add(new(path, attr.VendorID, attr.ProductID, caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, 0));
        }
        return result;
    }

    /// Device interface paths of every present HID collection.
    public static List<string> EnumerateHidPaths()
    {
        HidD_GetHidGuid(out var guid);
        var result = new List<string>();
        nint set = SetupDiGetClassDevsW(ref guid, 0, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1) return result;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = sizeof(SP_DEVICE_INTERFACE_DATA) };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref data, null, 0, out int size, 0);
                var buf = new byte[size];
                fixed (byte* p = buf)
                {
                    *(int*)p = IntPtr.Size == 8 ? 8 : 6; // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, p, size, out _, 0))
                        result.Add(new string((char*)(p + 4)));
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
