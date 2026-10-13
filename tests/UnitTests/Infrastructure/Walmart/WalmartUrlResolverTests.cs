using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Walmart;

public class WalmartUrlResolverTests
{
    // Contract server addresses, verbatim from api-specs/walmart.
    private const string InventoryServer = "https://marketplace.walmart.com/seller/v1";
    private const string AuthServer = "https://marketplace.walmart.com/seller";

    [Fact]
    public void WithoutOverrideUsesContractServerVerbatim()
    {
        var resolver = new WalmartUrlResolver(baseUrlOverride: null);

        var uri = resolver.Resolve(InventoryServer, "/inventory/v4/items/ESHOP-7");

        Assert.Equal("https://marketplace.walmart.com/seller/v1/inventory/v4/items/ESHOP-7", uri.ToString());
    }

    [Fact]
    public void OverrideReplacesSchemeHostAndPortButKeepsBasePathAndPath()
    {
        var resolver = new WalmartUrlResolver("http://localhost:9000");

        var uri = resolver.Resolve(InventoryServer, "/inventory/v4/items/ESHOP-7");

        // Scheme/host/port swapped; the /seller/v1 base path and the rest of the path are kept.
        Assert.Equal("http://localhost:9000/seller/v1/inventory/v4/items/ESHOP-7", uri.ToString());
    }

    [Fact]
    public void OverrideAppliesToAuthServerToo()
    {
        var resolver = new WalmartUrlResolver("https://walmart-sim.example.com:8443");

        var uri = resolver.Resolve(AuthServer, "/auth/v4/token");

        Assert.Equal("https://walmart-sim.example.com:8443/seller/auth/v4/token", uri.ToString());
    }

    [Fact]
    public void OverrideWithoutPortUsesSchemeDefaultPort()
    {
        var resolver = new WalmartUrlResolver("https://host.internal");

        var uri = resolver.Resolve(InventoryServer, "/inventory/v4/items");

        Assert.Equal("https://host.internal/seller/v1/inventory/v4/items", uri.ToString());
    }

    [Fact]
    public void QueryStringIsPreserved()
    {
        var resolver = new WalmartUrlResolver("http://localhost:9000");

        var uri = resolver.Resolve(InventoryServer, "/inventory/v4/items?pageSize=500&nextToken=abc");

        Assert.Equal("http://localhost:9000/seller/v1/inventory/v4/items?pageSize=500&nextToken=abc", uri.ToString());
    }
}
