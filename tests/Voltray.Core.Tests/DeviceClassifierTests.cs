using Voltray.Core;

namespace Voltray.Core.Tests;

public class DeviceClassifierTests
{
    [Theory]
    [InlineData("Galaxy Buds2 Pro", DeviceCategory.Earbuds)]
    [InlineData("WF-1000XM5", DeviceCategory.Earbuds)]
    [InlineData("Sony WH-1000XM5", DeviceCategory.Headphones)]
    [InlineData("HyperX Cloud Alpha Wireless", DeviceCategory.Headphones)]
    [InlineData("G Pro X Superlight", DeviceCategory.Mouse)]
    [InlineData("Razer Viper V2 Pro", DeviceCategory.Mouse)]
    [InlineData("Keychron K2", DeviceCategory.Keyboard)]
    [InlineData("Xbox Wireless Controller", DeviceCategory.Controller)]
    public void Guesses_category_from_name(string name, DeviceCategory expected) =>
        Assert.Equal(expected, DeviceClassifier.FromName(name));

    [Fact]
    public void Unknown_names_stay_unclassified() => Assert.Null(DeviceClassifier.FromName("ACME 3000"));

    [Fact]
    public void Provider_reported_category_is_not_overwritten_by_name()
    {
        var d = new DeviceInfo("x", "Some Buds", null, DeviceCategory.Headphones, Confidence.Reported, "test");
        Assert.Equal(DeviceCategory.Headphones, DeviceClassifier.Refine(d).Category);
    }
}
