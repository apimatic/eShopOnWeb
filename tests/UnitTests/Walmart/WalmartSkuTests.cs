using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartSkuTests
{
    [Fact]
    public void ForCatalogItem_formats_with_prefix()
    {
        Assert.Equal("ESHOP-7", WalmartSku.ForCatalogItem(7));
    }

    [Theory]
    [InlineData("ESHOP-7", true, 7)]
    [InlineData("ESHOP-123", true, 123)]
    [InlineData("ESHOP-0", false, 0)]
    [InlineData("ESHOP-", false, 0)]
    [InlineData("ESHOP-abc", false, 0)]
    [InlineData("OTHER-7", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("ESHOP-7 ", false, 0)]
    public void TryGetCatalogItemId_parses_only_valid_eshop_skus(string sku, bool expected, int expectedId)
    {
        var ok = WalmartSku.TryGetCatalogItemId(sku, out var id);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(expectedId, id);
        }
    }
}
