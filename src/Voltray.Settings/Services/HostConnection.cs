using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.UI.Dispatching;
using Voltray.Core;
using Voltray.Ipc;

namespace Voltray.Settings.Services;

/// Connection to the background Voltray.exe. Reconnects on its own; events are raised on the UI thread.
public sealed class HostConnection(DispatcherQueue ui)
{
    IpcChannel? _channel;

    public StateDto? State { get; private set; }
    public bool IsConnected => _channel != null;

    public event Action<StateDto>? StateReceived;
    public event Action<bool>? ConnectionChanged;

    public void Start() => _ = RunAsync();

    async Task RunAsync()
    {
        while (true)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", IpcChannel.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(1500);
                var channel = new IpcChannel(pipe);
                _channel = channel;
                ui.TryEnqueue(() => ConnectionChanged?.Invoke(true));
                await channel.SendAsync(new Message(MessageType.Subscribe));
                while (await channel.ReceiveAsync() is { } m)
                    if (m is { Type: MessageType.State, State: { } s })
                        ui.TryEnqueue(() => { State = s; StateReceived?.Invoke(s); });
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }

            bool wasConnected = _channel != null;
            if (_channel is { } c) await c.DisposeAsync();
            _channel = null;
            if (wasConnected) ui.TryEnqueue(() => ConnectionChanged?.Invoke(false));
            await Task.Delay(1500);
        }
    }

    Task SendAsync(Message m) => _channel?.SendAsync(m) ?? Task.CompletedTask;

    public Task SetSettingsAsync(Ipc.Settings settings) => SendAsync(new Message(MessageType.SetSettings, Settings: settings));
    public Task SetCategoryAsync(string deviceId, DeviceCategory? category) => SendAsync(new Message(MessageType.SetCategory, DeviceId: deviceId, Category: category));
    public Task RefreshAsync() => SendAsync(new Message(MessageType.Refresh));

    /// Applies <paramref name="change"/> to a copy of the current settings and sends it.
    public Task ChangeSettingsAsync(Action<Ipc.Settings> change)
    {
        if (State is null) return Task.CompletedTask;
        var s = State.Settings.Clone();
        change(s);
        return SetSettingsAsync(s);
    }

    /// Starts the background app when it is not running (the window was opened on its own).
    public static void StartHost()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "..", "Voltray.exe"), // installed layout: Voltray.exe + settings\
            Path.Combine(AppContext.BaseDirectory, "Voltray.exe"),
        ];
        if (candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) is { } exe)
            Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false });
    }
}
