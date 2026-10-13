using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Walmart;

public class WalmartSkuTests
{
    [Theory]
    [InlineData(7, "ESHOP-7")]
    [InlineData(1, "ESHOP-1")]
    [InlineData(123, "ESHOP-123")]
    public void ForCatalogItemBuildsSku(int id, string expected)
    {
        Assert.Equal(expected, WalmartSku.ForCatalogItem(id));
    }

    [Theory]
    [InlineData("ESHOP-7", true, 7)]
    [InlineData("ESHOP-123", true, 123)]
    [InlineData("ESHOP-0", false, 0)]
    [InlineData("ESHOP-", false, 0)]
    [InlineData("ESHOP-abc", false, 0)]
    [InlineData("eshop-7", false, 0)]
    [InlineData("SKU-7", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    [InlineData("ESHOP--1", false, 0)]
    public void TryParseCatalogItemId(string? sku, bool expectedOk, int expectedId)
    {
        var ok = WalmartSku.TryParseCatalogItemId(sku, out var id);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void RoundTrips()
    {
        var sku = WalmartSku.ForCatalogItem(55);
        Assert.True(WalmartSku.TryParseCatalogItemId(sku, out var id));
        Assert.Equal(55, id);
    }
}
