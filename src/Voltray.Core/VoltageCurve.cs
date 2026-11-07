namespace Voltray.Core;

/// Converts a single-cell Li-ion voltage to an approximate percentage, for devices that
/// only report millivolts. Generic curve until per-model curves exist.
public static class VoltageCurve
{
    static readonly (int Mv, int Pct)[] Generic =
        [(4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50), (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3500, 0)];

    public static int Estimate(int millivolts)
    {
        if (millivolts >= Generic[0].Mv) return 100;
        for (int i = 1; i < Generic.Length; i++)
            if (millivolts >= Generic[i].Mv)
            {
                var (hv, hp) = Generic[i - 1];
                var (lv, lp) = Generic[i];
                return lp + (millivolts - lv) * (hp - lp) / (hv - lv);
            }
        return 0;
    }
}
