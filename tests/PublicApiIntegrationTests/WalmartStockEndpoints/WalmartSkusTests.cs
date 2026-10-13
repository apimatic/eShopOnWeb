using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

[TestClass]
public class WalmartSkusTests
{
    [TestMethod]
    public void ForCatalogItem_UsesEshopPrefix()
    {
        Assert.AreEqual("ESHOP-7", WalmartSkus.ForCatalogItem(7));
    }

    [TestMethod]
    public void TryGetCatalogItemId_ParsesOwnSku()
    {
        Assert.IsTrue(WalmartSkus.TryGetCatalogItemId("ESHOP-42", out var id));
        Assert.AreEqual(42, id);
    }

    [TestMethod]
    [DataRow("ACME-1")]
    [DataRow("ESHOP-")]
    [DataRow("ESHOP-0")]
    [DataRow("ESHOP--3")]
    [DataRow("ESHOP-abc")]
    [DataRow("")]
    [DataRow(null)]
    public void TryGetCatalogItemId_RejectsForeignOrMalformedSkus(string? sku)
    {
        Assert.IsFalse(WalmartSkus.TryGetCatalogItemId(sku, out _));
    }
}
