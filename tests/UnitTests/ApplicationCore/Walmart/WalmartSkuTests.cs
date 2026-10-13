using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Walmart;

public class WalmartSkuTests
{
    [Fact]
    public void ForCatalogItemId_UsesEShopPrefix()
    {
        Assert.Equal("ESHOP-7", WalmartSku.ForCatalogItemId(7));
    }

    [Theory]
    [InlineData("ESHOP-7", true, 7)]
    [InlineData("ESHOP-1", true, 1)]
    [InlineData("eshop-42", true, 42)]
    [InlineData("ESHOP-0", false, 0)]
    [InlineData("ESHOP--3", false, 0)]
    [InlineData("ESHOP-abc", false, 0)]
    [InlineData("FOREIGN-7", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryGetCatalogItemId_ParsesOnlyEShopSkus(string? sku, bool expectedOk, int expectedId)
    {
        var ok = WalmartSku.TryGetCatalogItemId(sku, out var id);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedId, id);
    }
}
