using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Voltray.Ipc;

/// Framing shared by both ends: one JSON <see cref="Message"/> per line.
public sealed class IpcChannel(PipeStream pipe) : IAsyncDisposable
{
    /// Per user and per session, and only the current user may connect (PipeOptions.CurrentUserOnly).
    public static readonly string PipeName = $"Voltray.{Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

    readonly StreamReader _reader = new(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    readonly SemaphoreSlim _writeLock = new(1, 1);

    public PipeStream Pipe { get; } = pipe;

    public async Task SendAsync(Message message, CancellationToken ct = default)
    {
        var line = JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.Message);
        await _writeLock.WaitAsync(ct);
        try
        {
            await Pipe.WriteAsync(line, ct);
            await Pipe.WriteAsync("\n"u8.ToArray(), ct);
            await Pipe.FlushAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    /// Null when the other side closed the pipe.
    public async Task<Message?> ReceiveAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line is null) return null;
            if (line.Length == 0) continue;
            return JsonSerializer.Deserialize(line, IpcJson.Default.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await Pipe.DisposeAsync();
    }
}
