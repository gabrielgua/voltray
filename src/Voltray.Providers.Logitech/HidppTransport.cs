using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using static Voltray.Platform.Hid.HidNative;

namespace Voltray.Providers.Logitech;

/// One HID++ endpoint (receiver, wired or Bluetooth device): its long (0x11) collection and,
/// when present, its short (0x10) one, which carries HID++ 1.0 errors and connection notifications.
public sealed record HidppEndpoint(ushort Vid, ushort Pid, string? ShortPath, string LongPath, int LongLen, int ShortLen);

public sealed class HidppException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;
}

/// Request/response over HID++, opened in shared mode so it coexists with G HUB.
/// Replies are matched by (device index, feature index, function, software id); G HUB uses
/// a different software id, so its traffic is ignored here.
public sealed partial class HidppTransport : IDisposable
{
    public const byte ReportShort = 0x10, ReportLong = 0x11;
    public const byte SoftwareId = 0x0D;

    readonly FileStream _long;
    readonly FileStream? _short;
    readonly CancellationTokenSource _cts = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _pending = new();
    readonly SemaphoreSlim _writeLock = new(1, 1);

    /// Unsolicited reports (software id 0): battery changes, connection notifications.
    public event Action<byte[]>? Notification;

    public HidppEndpoint Endpoint { get; }

    public HidppTransport(HidppEndpoint endpoint)
    {
        Endpoint = endpoint;
        _long = Open(endpoint.LongPath);
        if (endpoint.ShortPath != null) _short = Open(endpoint.ShortPath);
        _ = ReadLoop(_long, endpoint.LongLen);
        if (_short != null) _ = ReadLoop(_short, endpoint.ShortLen);
    }

    static FileStream Open(string path)
    {
        SafeFileHandle h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, 0);
        if (h.IsInvalid) throw new IOException($"CreateFile failed ({Marshal.GetLastPInvokeError()}): {path}");
        return new FileStream(h, FileAccess.ReadWrite, 0, isAsync: true);
    }

    static int Key(byte dev, byte feat, byte funcSw) => dev << 16 | feat << 8 | funcSw;

    async Task ReadLoop(FileStream s, int len)
    {
        var buf = new byte[len];
        while (!_cts.IsCancellationRequested)
        {
            int n;
            try { n = await s.ReadAsync(buf, _cts.Token); }
            catch { return; } // device unplugged or disposed
            if (n >= 4) Dispatch(buf.AsSpan(0, n).ToArray());
        }
    }

    void Dispatch(byte[] r)
    {
        byte dev = r[1];
        // HID++ 2.0 error: 11 dev FF feat func|sw err
        if (r[0] == ReportLong && r[2] == 0xFF && _pending.TryRemove(Key(dev, r[3], r[4]), out var e2))
        { e2.TrySetException(new HidppException($"HID++ 2.0 error 0x{r[5]:X2}", r[5])); return; }
        // HID++ 1.0 error: 10 dev 8F feat func|sw err (0x09 = device not connected)
        if (r[0] == ReportShort && r[2] == 0x8F && _pending.TryRemove(Key(dev, r[3], r[4]), out var e1))
        { e1.TrySetException(new HidppException($"HID++ 1.0 error 0x{r[5]:X2}", r[5])); return; }
        if (_pending.TryRemove(Key(dev, r[2], r[3]), out var ok)) { ok.TrySetResult(r); return; }
        if ((r[3] & 0x0F) == 0 || r[2] < 0x80 && r[0] == ReportShort) Notification?.Invoke(r);
    }

    /// Sends a long HID++ 2.0 request and returns the 16 parameter bytes of the reply.
    public async Task<byte[]> RequestAsync(byte device, byte featureIndex, byte function, ReadOnlyMemory<byte> args = default, int timeoutMs = 1500)
    {
        byte funcSw = (byte)(function << 4 | SoftwareId);
        var msg = new byte[Endpoint.LongLen];
        msg[0] = ReportLong; msg[1] = device; msg[2] = featureIndex; msg[3] = funcSw;
        args.Span.CopyTo(msg.AsSpan(4));

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int key = Key(device, featureIndex, funcSw);
        _pending[key] = tcs;

        // The timeout covers the write too: a write to a receiver can stall (seen while the device sleeps).
        using var timeout = new CancellationTokenSource(timeoutMs);
        await using var expiry = timeout.Token.Register(() => { if (_pending.TryRemove(key, out var t)) t.TrySetException(new TimeoutException()); });
        try
        {
            await _writeLock.WaitAsync(timeout.Token);
            try { await _long.WriteAsync(msg, timeout.Token); }
            finally { _writeLock.Release(); }
        }
        catch (OperationCanceledException) { _pending.TryRemove(key, out _); throw new TimeoutException(); }
        return (await tcs.Task).AsSpan(4).ToArray();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _long.Dispose();
        _short?.Dispose();
    }

    /// Finds Logitech HID++ endpoints: vendor collections FF00:0001/0002 (receivers, USB)
    /// and FF43:0202 (Bluetooth / Bolt direct).
    public static List<HidppEndpoint> Discover()
    {
        var byParent = new Dictionary<string, (string? s, string? l, int sl, int ll, ushort vid, ushort pid)>();
        foreach (var c in EnumerateCollections(0x046D))
        {
            bool isShort = c.UsagePage == 0xFF00 && c.Usage == 0x0001;
            bool isLong = (c.UsagePage == 0xFF00 && c.Usage == 0x0002) || (c.UsagePage == 0xFF43 && c.Usage == 0x0202);
            if (!isShort && !isLong) continue;

            // Collections of one USB interface differ in "&col0N" and in the last instance-id segment:
            // ...mi_02&col01#9&25d5f166&0&0000#{guid} / ...mi_02&col02#9&25d5f166&0&0001#{guid}
            string parent = CollectionSuffix().Replace(c.Path.ToLowerInvariant(), "");
            parent = parent[..parent.LastIndexOf('#')];
            parent = parent[..parent.LastIndexOf('&')];
            byParent.TryGetValue(parent, out var e);
            e.vid = c.Vid; e.pid = c.Pid;
            if (isShort) { e.s = c.Path; e.sl = c.InputLen; }
            else { e.l = c.Path; e.ll = c.OutputLen; }
            byParent[parent] = e;
        }
        return [.. byParent.Values.Where(e => e.l != null)
            .Select(e => new HidppEndpoint(e.vid, e.pid, e.s, e.l!, e.ll, Math.Max(e.sl, 7)))];
    }

    [GeneratedRegex(@"&col\d+")]
    private static partial Regex CollectionSuffix();
}
