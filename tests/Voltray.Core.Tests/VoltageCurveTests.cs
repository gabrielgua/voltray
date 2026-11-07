using Voltray.Core;

namespace Voltray.Core.Tests;

public class VoltageCurveTests
{
    [Theory]
    [InlineData(4200, 100)]
    [InlineData(4186, 100)]
    [InlineData(3811, 50)]
    [InlineData(3400, 0)]
    public void Maps_known_points(int mv, int pct) => Assert.Equal(pct, VoltageCurve.Estimate(mv));

    [Fact]
    public void Is_monotonic()
    {
        int last = -1;
        for (int mv = 3400; mv <= 4250; mv += 5)
        {
            int p = VoltageCurve.Estimate(mv);
            Assert.True(p >= last, $"{mv} mV -> {p}% after {last}%");
            last = p;
        }
    }
}
