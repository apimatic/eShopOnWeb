using Microsoft.eShopWeb.ApplicationCore.Marketplace;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Marketplace;

public class WalmartSkuTests
{
    [Fact]
    public void ForCatalogItemFormatsSku()
    {
        Assert.Equal("ESHOP-7", WalmartSku.ForCatalogItem(7));
    }

    [Theory]
    [InlineData("ESHOP-7", true, 7)]
    [InlineData("ESHOP-123", true, 123)]
    [InlineData("ESHOP-", false, 0)]
    [InlineData("ESHOP-abc", false, 0)]
    [InlineData("OTHER-7", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryGetCatalogItemId(string? sku, bool expected, int expectedId)
    {
        var ok = WalmartSku.TryGetCatalogItemId(sku, out var id);
        Assert.Equal(expected, ok);
        Assert.Equal(expectedId, id);
    }
}
