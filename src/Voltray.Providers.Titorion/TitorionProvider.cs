using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Voltray.Core;
using static Voltray.Platform.Hid.HidNative;

namespace Voltray.Providers.Titorion;

/// Titorion 2.4 GHz keyboards behind their "Wireless Receiver" (056D:C077 / C088). The battery is
/// read with the same read-only query the vendor web app sends: command 0x1F ("getPoorNum") on
/// the receiver's vendor collection FF87, report 6. The keyboard never pushes it, so it is polled.
public sealed partial class TitorionProvider : IBatteryProvider
{
    const ushort Vid = 0x056D;
    static readonly ushort[] Pids = [0xC077, 0xC088];
    const ushort UsagePage = 0xFF87;
    internal const byte ReportId = 6, CmdBattery = 0x1F;

    readonly Dictionary<string, string> _known = []; // collection path -> device id
    readonly SemaphoreSlim _busy = new(1, 1);
    IDeviceSink? _sink;

    public string Id => "titorion";

    public TimeSpan MinimumPollInterval => TimeSpan.FromMinutes(1);

    public Task StartAsync(IDeviceSink sink, CancellationToken ct)
    {
        _sink = sink;
        return RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var present = EnumerateCollections(Vid)
                .Where(c => c.Error == 0 && c.UsagePage == UsagePage && Pids.Contains(c.Pid))
                .ToList();

            foreach (var gone in _known.Keys.Where(p => present.All(c => c.Path != p)).ToList())
            {
                _sink!.Disconnected(_known[gone]);
                _known.Remove(gone);
                Log.Info("titorion: receiver removed");
            }

            foreach (var c in present)
            {
                if (!_known.TryGetValue(c.Path, out var id))
                {
                    id = $"titorion:{c.Pid:X4}-{InstanceOf(c.Path)}";
                    _known[c.Path] = id;
                    Log.Info($"titorion: receiver {c.Pid:X4} found, id={id}");
                    _sink!.Upsert(new DeviceInfo(id, "Titorion keyboard", "Titorion", DeviceCategory.Keyboard, Confidence.Reported, Id));
                }
                var reading = await QueryAsync(c, ct);
                if (reading is null) _sink!.Disconnected(id);
                else _sink!.Report(id, reading);
            }
        }
        finally { _busy.Release(); }
    }

    /// Sends the battery query and waits for its reply. null = no answer (keyboard off or asleep).
    static async Task<BatteryReading?> QueryAsync(HidCollection c, CancellationToken ct)
    {
        SafeFileHandle h = CreateFile(c.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, 0);
        if (h.IsInvalid) { Log.Info($"titorion: cannot open receiver ({Marshal.GetLastPInvokeError()})"); return null; }
        await using var s = new FileStream(h, FileAccess.ReadWrite, 0, isAsync: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(1500);
        try
        {
            var buf = new byte[Math.Max(c.InputLen, 64)];
            var read = s.ReadAsync(buf, timeout.Token).AsTask(); // listen before asking: the reply is quick
            await s.WriteAsync(BuildQuery(Math.Max(c.OutputLen, 64)), timeout.Token);
            while (true)
            {
                int n = await read;
                if (ParseReply(buf.AsSpan(0, n), DateTimeOffset.Now) is { } r) return r;
                read = s.ReadAsync(buf, timeout.Token).AsTask(); // some other report on the collection
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException e) { Log.Info($"titorion: query failed ({e.Message})"); return null; }
    }

    /// Report 6: [06, 1F, 00, 00, 19, sumLo, sumHi, 00, 0...], where the sum covers the 7 command
    /// bytes and 0x19 is the reply length the web app asks for (32) minus 7.
    internal static byte[] BuildQuery(int length)
    {
        var pkt = new byte[length];
        pkt[0] = ReportId;
        pkt[1] = CmdBattery;
        pkt[4] = 32 - 7;
        int sum = 0;
        for (int i = 1; i <= 7; i++) sum += pkt[i];
        pkt[5] = (byte)sum;
        pkt[6] = (byte)(sum >> 8);
        return pkt;
    }

    /// Reply, with the report id: [06, 1F, ..., percent @8, state @9 (0 normal, 1 charging, 2 full), low @10].
    internal static BatteryReading? ParseReply(ReadOnlySpan<byte> r, DateTimeOffset now)
    {
        if (r.Length < 11 || r[0] != ReportId || r[1] != CmdBattery || r[8] > 100) return null;
        int pct = r[8];
        var charge = r[9] switch { 1 => ChargeState.Charging, 2 => ChargeState.Full, _ => ChargeState.Discharging };
        return new BatteryReading(pct, BatteryReading.LevelFromPercent(pct), charge, now, "Titorion 2.4G");
    }

    /// "...mi_01&col05#a&38d7d542&0&0004#{guid}" -> "a-38d7d542": stable per receiver and USB port.
    internal static string InstanceOf(string path)
    {
        var m = InstanceRegex().Match(path);
        return m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}".ToLowerInvariant() : "0";
    }

    [GeneratedRegex(@"#([0-9a-f]+)&([0-9a-f]+)&", RegexOptions.IgnoreCase)]
    private static partial Regex InstanceRegex();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
