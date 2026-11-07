using System.Text.Json;
using System.Text.Json.Serialization;
using Voltray.Core;

namespace Voltray;

/// Keeps the battery history in %LocalAppData%\Voltray\history.json. Writes are batched (at most
/// once a minute, plus on exit) because readings arrive often and the data is only a nicety.
internal sealed class HistoryStore
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltray", "history.json");
    static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    bool _dirty;

    public BatteryHistory History { get; } = Load();

    static BatteryHistory Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize(File.ReadAllText(FilePath), HistoryJson.Default.BatteryHistory) is { } h)
            {
                h.Prune(DateTimeOffset.Now);
                return h;
            }
        }
        catch (Exception e) when (e is IOException or JsonException) { Log.Error("reading battery history", e); }
        return new BatteryHistory();
    }

    /// Records the current state of every device; saves if enough time passed since the last write.
    public void Record(IEnumerable<DeviceState> devices)
    {
        var now = DateTimeOffset.Now;
        foreach (var d in devices)
            if (d.Battery?.Percent is int p)
                _dirty |= History.Record(d.Info.Id, new BatterySample(now, p, d.Battery.Charge is ChargeState.Charging, d.Connected));
        if (_dirty && now - _lastSave >= SaveEvery) Save();
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            History.Prune(DateTimeOffset.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(History, HistoryJson.Default.BatteryHistory));
            _dirty = false;
            _lastSave = DateTimeOffset.Now;
        }
        catch (IOException e) { Log.Error("saving battery history", e); }
    }

    public BatteryEstimate? Estimate(string deviceId) =>
        BatteryEstimator.Estimate(History.For(deviceId), DateTimeOffset.Now, History.LearnedRates.TryGetValue(deviceId, out var r) ? r : null);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BatteryHistory))]
internal sealed partial class HistoryJson : JsonSerializerContext;
