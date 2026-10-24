using Bielu.Microservices.Orchestrator.Models;
using Bielu.Microservices.Orchestrator.Utilities;
using Shouldly;
using Xunit;

namespace Bielu.Microservices.Orchestrator.Tests;

/// <summary>
/// Tests for <see cref="IpAllocator"/>.
/// </summary>
public class IpAllocatorTests
{
    private static NetworkIpamConfig Config(string subnet, string gateway = "", string ipRange = "", params (string Key, string Value)[] aux)
    {
        var config = new NetworkIpamConfig { Subnet = subnet, Gateway = gateway, IPRange = ipRange };
        foreach (var (key, value) in aux)
        {
            config.AuxAddress[key] = value;
        }

        return config;
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_NullConfigs_ReturnsNull()
    {
        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(null, ["10.0.0.2"]);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_FreeSubnet_SkipsGatewayAndReturnsFirstHost()
    {
        // Arrange
        var configs = new[] { Config("172.19.0.0/16", gateway: "172.19.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, null);

        // Assert
        result.ShouldBe("172.19.0.2");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_InUseAddressesWithCidrSuffix_AreSkipped()
    {
        // Arrange
        var configs = new[] { Config("172.19.0.0/16", gateway: "172.19.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, ["172.19.0.2/16", "172.19.0.3", "", "not-an-ip", "fd00::5"]);

        // Assert
        result.ShouldBe("172.19.0.4");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_AuxAddresses_AreSkipped()
    {
        // Arrange
        var configs = new[] { Config("10.1.0.0/24", "10.1.0.1", "", ("router", "10.1.0.2"), ("dns", "10.1.0.3")) };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, []);

        // Assert
        result.ShouldBe("10.1.0.4");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_IPRange_ConstrainsTheStartOfAllocation()
    {
        // Arrange
        var configs = new[] { Config("10.0.0.0/16", gateway: "10.0.0.1", ipRange: "10.0.5.0/24") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, ["10.0.5.0"]);

        // Assert
        result.ShouldBe("10.0.5.1");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_IPRange_ConstrainsTheEndOfAllocation()
    {
        // Arrange: the range 192.168.1.0/30 leaves only .1-.3 inside the /24.
        var configs = new[] { Config("192.168.1.0/24", gateway: "192.168.1.1", ipRange: "192.168.1.0/30") };

        // Act
        var lastFree = IpAllocator.TryAllocateNextFreeIPv4(configs, ["192.168.1.2"]);
        var exhausted = IpAllocator.TryAllocateNextFreeIPv4(configs, ["192.168.1.2", "192.168.1.3"]);

        // Assert
        lastFree.ShouldBe("192.168.1.3");
        exhausted.ShouldBeNull();
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_InvalidIPRange_FallsBackToTheWholeSubnet()
    {
        // Arrange
        var configs = new[] { Config("10.2.0.0/24", gateway: "10.2.0.1", ipRange: "garbage") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, []);

        // Assert
        result.ShouldBe("10.2.0.2");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_SubnetExhausted_ReturnsNull()
    {
        // Arrange: a /30 has two usable hosts, the gateway and one in-use address.
        var configs = new[] { Config("10.3.0.0/30", gateway: "10.3.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, ["10.3.0.2"]);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_BroadcastAddress_IsNeverReturned()
    {
        // Arrange
        var configs = new[] { Config("10.4.0.0/30") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, ["10.4.0.1", "10.4.0.2"]);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_SingleHostSubnetWithoutPrefix_HasNoFreeAddress()
    {
        // Arrange: "10.5.0.7" parses as a /32.
        var configs = new[] { Config("10.5.0.7") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, []);

        // Assert
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-subnet/24")]
    [InlineData("10.0.0.0/40")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("fd00::/64")]
    public void TryAllocateNextFreeIPv4_UnusableSubnet_IsSkippedForTheNextConfig(string subnet)
    {
        // Arrange
        var configs = new[] { Config(subnet), Config("10.6.0.0/24", gateway: "10.6.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, []);

        // Assert
        result.ShouldBe("10.6.0.2");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_FirstSubnetFull_UsesTheNextConfig()
    {
        // Arrange
        var configs = new[] { Config("10.7.0.0/30", gateway: "10.7.0.1"), Config("10.8.0.0/24", gateway: "10.8.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, ["10.7.0.2"]);

        // Assert
        result.ShouldBe("10.8.0.2");
    }

    [Fact]
    public void TryAllocateNextFreeIPv6_Subnet_SkipsGatewayAndInUseAddresses()
    {
        // Arrange
        var configs = new[] { Config("172.20.0.0/16"), Config("fd00:1::/64", gateway: "fd00:1::1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv6(configs, ["fd00:1::2/64", "172.20.0.2"]);

        // Assert
        result.ShouldBe("fd00:1::3");
    }

    [Fact]
    public void TryAllocateNextFreeIPv6_LastAddressOfSubnet_IsAllocatable()
    {
        // Arrange: IPv6 has no broadcast, so the last address of a /127 is usable.
        var configs = new[] { Config("fd00:2::/127") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv6(configs, []);

        // Assert
        result.ShouldBe("fd00:2::1");
    }

    [Fact]
    public void TryAllocateNextFreeIPv6_OnlyIPv4Subnets_ReturnsNull()
    {
        // Arrange
        var configs = new[] { Config("10.9.0.0/24") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv6(configs, []);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void TryAllocateNextFreeIPv6_AddressWithHighBitSet_RoundTripsWithoutSignIssues()
    {
        // Arrange: addresses starting with 0xff would read as negative without the unsigned padding.
        var configs = new[] { Config("ff05::/120", gateway: "ff05::1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv6(configs, []);

        // Assert
        result.ShouldBe("ff05::2");
    }

    [Fact]
    public void TryAllocateNextFreeIPv4_AddressWithHighBitSet_RoundTripsWithoutSignIssues()
    {
        // Arrange
        var configs = new[] { Config("240.0.0.0/24", gateway: "240.0.0.1") };

        // Act
        var result = IpAllocator.TryAllocateNextFreeIPv4(configs, []);

        // Assert
        result.ShouldBe("240.0.0.2");
    }
}
