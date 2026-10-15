using CameraMaintainer.Onvif;

namespace CameraMaintainer.Tests;

public sealed class DiscoveryClientTests
{
    [Fact]
    public void BuildScanAddresses_UsesTheCidrNetworkAndCapsLargeRanges()
    {
        var addresses = DiscoveryClient.BuildScanAddresses("192.168.4.77/24");

        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.4.1", addresses[0]);
        Assert.Equal("192.168.4.254", addresses[^1]);
    }

    [Theory]
    [InlineData("10.20.30.40/32", 1, "10.20.30.40", "10.20.30.40")]
    [InlineData("10.20.30.40/31", 2, "10.20.30.40", "10.20.30.41")]
    [InlineData("10.20.30.40", 254, "10.20.30.1", "10.20.30.254")]
    public void BuildScanAddresses_HandlesSmallAndDefaultRanges(
        string subnet,
        int count,
        string first,
        string last)
    {
        var addresses = DiscoveryClient.BuildScanAddresses(subnet);

        Assert.Equal(count, addresses.Count);
        Assert.Equal(first, addresses[0]);
        Assert.Equal(last, addresses[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("192.168.1.1/33")]
    public void BuildScanAddresses_RejectsInvalidRanges(string subnet)
    {
        Assert.ThrowsAny<ArgumentException>(() => DiscoveryClient.BuildScanAddresses(subnet));
    }
}
