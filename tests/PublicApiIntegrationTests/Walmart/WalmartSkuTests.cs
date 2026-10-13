using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class WalmartSkuTests
{
    [TestMethod]
    public void ForCatalogItem_UsesEshopPrefix()
    {
        Assert.AreEqual("ESHOP-7", WalmartSku.ForCatalogItem(7));
    }

    [TestMethod]
    public void TryGetCatalogItemId_ParsesWellFormedSku()
    {
        Assert.IsTrue(WalmartSku.TryGetCatalogItemId("ESHOP-42", out var id));
        Assert.AreEqual(42, id);
    }

    [TestMethod]
    public void TryGetCatalogItemId_RejectsForeignOrMalformed()
    {
        Assert.IsFalse(WalmartSku.TryGetCatalogItemId("SKU-1", out _));
        Assert.IsFalse(WalmartSku.TryGetCatalogItemId("ESHOP-", out _));
        Assert.IsFalse(WalmartSku.TryGetCatalogItemId("ESHOP-abc", out _));
        Assert.IsFalse(WalmartSku.TryGetCatalogItemId(null, out _));
    }
}
