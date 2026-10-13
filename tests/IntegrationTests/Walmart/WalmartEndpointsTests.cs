using Microsoft.eShopWeb.Infrastructure.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Walmart;

public class WalmartEndpointsTests
{
    [Fact]
    public void UsesContractServersWhenNoOverride()
    {
        var endpoints = new WalmartEndpoints(null);

        Assert.Equal("https://marketplace.walmart.com/seller/auth/v4/token", endpoints.TokenEndpoint.ToString());
        Assert.Equal("https://marketplace.walmart.com/seller/v1/inventory/v4/items/ESHOP-7", endpoints.InventoryItem("ESHOP-7").ToString());
        Assert.Equal("https://marketplace.walmart.com/seller/v1/inventory/v4/items/bulk", endpoints.BulkInventory().ToString());
    }

    [Fact]
    public void OverrideReplacesSchemeHostPortButKeepsPath()
    {
        var endpoints = new WalmartEndpoints("http://localhost:39070");

        // Authority swapped; base path (/seller, /seller/v1) and operation path preserved exactly.
        Assert.Equal("http://localhost:39070/seller/auth/v4/token", endpoints.TokenEndpoint.ToString());
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items/ESHOP-7", endpoints.InventoryItem("ESHOP-7").ToString());
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items/bulk", endpoints.BulkInventory().ToString());
    }

    [Fact]
    public void OverrideWithoutPortUsesSchemeDefault()
    {
        var endpoints = new WalmartEndpoints("https://walmart.test");
        Assert.Equal("https://walmart.test/seller/auth/v4/token", endpoints.TokenEndpoint.ToString());
    }

    [Fact]
    public void InventorySummariesIncludesPagingQuery()
    {
        var endpoints = new WalmartEndpoints("http://localhost:39070");
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items?pageSize=500", endpoints.InventorySummaries(500, null).ToString());
        Assert.Equal("http://localhost:39070/seller/v1/inventory/v4/items?pageSize=500&nextToken=abc", endpoints.InventorySummaries(500, "abc").ToString());
    }
}
