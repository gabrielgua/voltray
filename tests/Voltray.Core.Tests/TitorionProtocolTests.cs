using Voltray.Core;
using Voltray.Providers.Titorion;

public class TitorionProtocolTests
{
    [Fact]
    public void Query_matches_the_web_app_packet()
    {
        var q = TitorionProvider.BuildQuery(64);
        Assert.Equal(64, q.Length);
        Assert.Equal(new byte[] { 0x06, 0x1F, 0x00, 0x00, 0x19, 0x38, 0x00, 0x00 }, q[..8]);
        Assert.All(q[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Reply_seen_on_hardware_is_83_percent_discharging()
    {
        var r = new byte[64];
        new byte[] { 0x06, 0x1F, 0x00, 0x00, 0x19, 0xE0, 0x00, 0x55, 0x53, 0x00, 0x00 }.CopyTo(r, 0);
        var b = TitorionProvider.ParseReply(r, DateTimeOffset.Now);
        Assert.NotNull(b);
        Assert.Equal(83, b.Percent);
        Assert.Equal(ChargeState.Discharging, b.Charge);
    }

    [Theory]
    [InlineData(1, ChargeState.Charging)]
    [InlineData(2, ChargeState.Full)]
    public void Reply_state_byte(byte state, ChargeState expected)
    {
        var r = new byte[64]; r[0] = 6; r[1] = 0x1F; r[8] = 50; r[9] = state;
        Assert.Equal(expected, TitorionProvider.ParseReply(r, DateTimeOffset.Now)!.Charge);
    }

    [Fact]
    public void Other_reports_are_ignored()
    {
        var r = new byte[64]; r[0] = 6; r[1] = 0x20; r[8] = 50;
        Assert.Null(TitorionProvider.ParseReply(r, DateTimeOffset.Now));
        r[1] = 0x1F; r[8] = 180;
        Assert.Null(TitorionProvider.ParseReply(r, DateTimeOffset.Now));
    }

    [Fact]
    public void Instance_id_comes_from_the_path()
    {
        Assert.Equal("a-38d7d542", TitorionProvider.InstanceOf(@"\\?\hid#vid_056d&pid_c077&mi_01&col05#a&38d7d542&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
    }
}
