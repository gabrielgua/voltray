using System.Text.Json.Serialization;
using Voltray.Core;

namespace Voltray.Ipc;

/// One device as the settings window sees it.
public sealed record DeviceDto(
    string Id,
    string Name,
    string? Manufacturer,
    DeviceCategory Category,
    Confidence CategoryConfidence,
    bool Connected,
    int? Percent,
    BatteryLevel Level,
    ChargeState Charge,
    string? Source,
    DateTimeOffset? UpdatedAt,
    bool ShownInTray,
    bool Notify);

public sealed record StateDto(string Version, List<DeviceDto> Devices, Settings Settings);

public enum MessageType
{
    /// Window → host: send the state now and again after every change.
    Subscribe,
    /// Host → window.
    State,
    /// Window → host: replace the settings.
    SetSettings,
    /// Window → host: user-chosen category for <see cref="Message.DeviceId"/> (null category = automatic).
    SetCategory,
    /// Window → host: poll every provider now.
    Refresh,
    /// Window → host: show a sample low-battery notification (<see cref="Message.Percent"/> and
    /// <see cref="Message.Text"/> preview one alert rule).
    TestNotification,
}

/// Newline-delimited JSON over a named pipe. One envelope type keeps the protocol trivially AOT-safe.
public sealed record Message(
    MessageType Type,
    StateDto? State = null,
    Settings? Settings = null,
    string? DeviceId = null,
    DeviceCategory? Category = null,
    int? Percent = null,
    string? Text = null);

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(Settings))]
public sealed partial class IpcJson : JsonSerializerContext;
