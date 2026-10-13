using Microsoft.eShopWeb.PublicApi.WalmartIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartIntegration;

[TestClass]
public class WalmartSkuTests
{
    [TestMethod]
    public void ForCatalogItem_BuildsEshopPrefixedSku()
    {
        Assert.AreEqual("ESHOP-7", WalmartSku.ForCatalogItem(7));
    }

    [TestMethod]
    public void TryGetCatalogItemId_ParsesEshopSku()
    {
        Assert.IsTrue(WalmartSku.TryGetCatalogItemId("ESHOP-42", out var id));
        Assert.AreEqual(42, id);
    }

    [DataTestMethod]
    [DataRow("WM-42")]
    [DataRow("ESHOP-")]
    [DataRow("ESHOP-0")]
    [DataRow("ESHOP--1")]
    [DataRow("ESHOP-abc")]
    [DataRow("")]
    [DataRow(null)]
    public void TryGetCatalogItemId_RejectsNonEshopSku(string? sku)
    {
        Assert.IsFalse(WalmartSku.TryGetCatalogItemId(sku, out _));
    }
}

[TestClass]
public class WalmartRebaseTests
{
    [TestMethod]
    public void Rebase_ReplacesSchemeHostPort_KeepsContractPath()
    {
        var result = WalmartServiceCollectionExtensions.Rebase(
            "https://marketplace.walmart.com/seller/v1", "http://localhost:39210");

        Assert.AreEqual("http://localhost:39210/seller/v1", result);
    }

    [TestMethod]
    public void Rebase_KeepsDefaultPort()
    {
        var result = WalmartServiceCollectionExtensions.Rebase(
            "https://marketplace.walmart.com/seller", "https://proxy.example.com");

        Assert.AreEqual("https://proxy.example.com/seller", result);
    }
}
