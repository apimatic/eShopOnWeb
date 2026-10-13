using Microsoft.eShopWeb.Infrastructure.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartEndpointsTests
{
    [Fact]
    public void WithoutOverride_UsesContractServer()
    {
        Assert.Equal("https://marketplace.walmart.com/seller/", WalmartEndpoints.ResolveAuthBase(null).ToString());
        Assert.Equal("https://marketplace.walmart.com/seller/v1/", WalmartEndpoints.ResolveInventoryBase(null).ToString());
    }

    [Fact]
    public void Override_ReplacesSchemeHostPort_PreservesContractPath()
    {
        const string over = "http://localhost:39190";

        // Each contract keeps its own base path after the host.
        Assert.Equal("http://localhost:39190/seller/", WalmartEndpoints.ResolveAuthBase(over).ToString());
        Assert.Equal("http://localhost:39190/seller/v1/", WalmartEndpoints.ResolveInventoryBase(over).ToString());
    }

    [Fact]
    public void Override_WithoutPort_UsesSchemeDefaultPort()
    {
        const string over = "https://walmart-mock.example.com";
        Assert.Equal("https://walmart-mock.example.com/seller/v1/", WalmartEndpoints.ResolveInventoryBase(over).ToString());
    }

    [Fact]
    public void Override_IgnoresAnyPathInOverride()
    {
        // Only scheme/host/port are taken from the override; a path on it is discarded.
        const string over = "http://localhost:39190/ignored/path";
        Assert.Equal("http://localhost:39190/seller/v1/", WalmartEndpoints.ResolveInventoryBase(over).ToString());
    }
}
