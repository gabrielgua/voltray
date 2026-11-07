using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Voltray.Settings;

public static partial class Program
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string? cls, string? title);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint h, int cmd);

    [STAThread]
    static int Main(string[] args)
    {
        // One window at a time: a second launch just brings the first one forward.
        using var single = new Mutex(true, @"Local\Voltray.Settings", out bool first);
        if (!first)
        {
            // --no-activate (automation) must never pull an existing window over what the user is doing.
            nint w = FindWindow("WinUIDesktopWin32WindowClass", MainWindow.WindowTitle);
            if (w != 0 && !args.Contains("--no-activate")) { ShowWindow(w, 9 /* SW_RESTORE */); SetForegroundWindow(w); }
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}
