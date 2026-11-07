using System.IO.Pipes;
using Voltray.Core;
using Voltray.Ipc;

namespace Voltray;

/// Named-pipe server the settings window talks to. Commands are handed to the host
/// (which applies them on its UI thread); state is pushed to every subscribed window.
internal sealed class IpcServer(Func<StateDto> currentState, Action<Message> onCommand)
{
    readonly Lock _lock = new();
    readonly List<IpcChannel> _subscribers = [];

    public void Start() => _ = AcceptLoopAsync();

    async Task AcceptLoopAsync()
    {
        while (true)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(IpcChannel.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
            }
            catch (Exception e)
            {
                Log.Error("ipc: accept failed", e);
                await Task.Delay(1000);
                continue;
            }
            _ = ServeAsync(new IpcChannel(pipe));
        }
    }

    async Task ServeAsync(IpcChannel channel)
    {
        try
        {
            while (await channel.ReceiveAsync() is { } message)
            {
                if (message.Type == MessageType.Subscribe)
                {
                    lock (_lock) _subscribers.Add(channel);
                    await channel.SendAsync(new Message(MessageType.State, currentState()));
                }
                else onCommand(message);
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or ObjectDisposedException) { }
        finally
        {
            lock (_lock) _subscribers.Remove(channel);
            await channel.DisposeAsync();
        }
    }

    public void Broadcast(StateDto state)
    {
        List<IpcChannel> targets;
        lock (_lock) targets = [.. _subscribers];
        foreach (var c in targets)
            _ = c.SendAsync(new Message(MessageType.State, state)).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
}
