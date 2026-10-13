using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Marketplace;

public class WalmartSkuTests
{
    [Theory]
    [InlineData(7, "ESHOP-7")]
    [InlineData(1, "ESHOP-1")]
    [InlineData(12345, "ESHOP-12345")]
    public void ForCatalogItemBuildsExpectedSku(int id, string expected)
    {
        Assert.Equal(expected, WalmartSku.ForCatalogItem(id));
    }

    [Theory]
    [InlineData("ESHOP-7", 7)]
    [InlineData("ESHOP-12", 12)]
    [InlineData("eshop-9", 9)] // case-insensitive prefix
    public void TryGetCatalogItemIdParsesEShopSkus(string sku, int expectedId)
    {
        Assert.True(WalmartSku.TryGetCatalogItemId(sku, out var id));
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("WALMART-ONLY-123")]
    [InlineData("ESHOP-")]
    [InlineData("ESHOP-abc")]
    [InlineData("ESHOP--1")]
    [InlineData(null)]
    [InlineData("")]
    public void TryGetCatalogItemIdRejectsNonEShopSkus(string? sku)
    {
        Assert.False(WalmartSku.TryGetCatalogItemId(sku, out _));
    }

    [Fact]
    public void RoundTrips()
    {
        var sku = WalmartSku.ForCatalogItem(42);
        Assert.True(WalmartSku.TryGetCatalogItemId(sku, out var id));
        Assert.Equal(42, id);
    }
}
