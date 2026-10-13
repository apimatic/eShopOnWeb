using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class WalmartEndpointResolverTest
{
    private static WalmartEndpointResolver Resolver(string? baseUrl) =>
        new(Options.Create(new WalmartOptions { BaseUrl = baseUrl }));

    [TestMethod]
    public void NoOverride_UsesContractServersVerbatim()
    {
        var r = Resolver(null);

        Assert.AreEqual("https://marketplace.walmart.com/seller/auth/v4/token", r.TokenUrl().ToString());
        Assert.AreEqual("https://marketplace.walmart.com/seller/v1/inventory/v4/items/ESHOP-7", r.InventoryItemUrl("ESHOP-7").ToString());
        Assert.AreEqual("https://marketplace.walmart.com/seller/v1/inventory/v4/items/bulk", r.InventoryBulkUrl().ToString());
    }

    [TestMethod]
    public void Override_ReplacesSchemeHostPort_PreservesContractPath()
    {
        var r = Resolver("http://localhost:39150");

        // Base path from each contract server (/seller, /seller/v1) is preserved; only authority changes.
        Assert.AreEqual("http://localhost:39150/seller/auth/v4/token", r.TokenUrl().ToString());
        Assert.AreEqual("http://localhost:39150/seller/v1/inventory/v4/items/ESHOP-7", r.InventoryItemUrl("ESHOP-7").ToString());
        Assert.AreEqual("http://localhost:39150/seller/v1/inventory/v4/items/bulk", r.InventoryBulkUrl().ToString());
    }

    [TestMethod]
    public void Override_WithoutPort_UsesSchemeDefault()
    {
        var r = Resolver("https://walmart-compat.example.com");

        Assert.AreEqual("https://walmart-compat.example.com/seller/auth/v4/token", r.TokenUrl().ToString());
        Assert.AreEqual("https://walmart-compat.example.com/seller/v1/inventory/v4/items/ESHOP-1", r.InventoryItemUrl("ESHOP-1").ToString());
    }

    [TestMethod]
    public void InventoryList_BuildsPagingQuery()
    {
        var r = Resolver("http://localhost:39150");

        Assert.AreEqual("http://localhost:39150/seller/v1/inventory/v4/items?pageSize=500", r.InventoryListUrl(500, null).ToString());
        Assert.AreEqual("http://localhost:39150/seller/v1/inventory/v4/items?pageSize=500&nextToken=abc", r.InventoryListUrl(500, "abc").ToString());
    }
}
