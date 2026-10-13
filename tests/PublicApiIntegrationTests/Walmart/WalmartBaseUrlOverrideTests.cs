using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Walmart;

[TestClass]
public class WalmartBaseUrlOverrideTests
{
    [TestMethod]
    public void NullOverride_ReturnsOriginalUnchanged()
    {
        const string original = "https://marketplace.walmart.com/seller/v1";
        Assert.AreEqual(original, WalmartBaseUrlOverride.Apply(original, null));
        Assert.AreEqual(original, WalmartBaseUrlOverride.Apply(original, "   "));
    }

    [TestMethod]
    public void Override_ReplacesSchemeHostPort_PreservesPath()
    {
        var result = WalmartBaseUrlOverride.Apply(
            "https://marketplace.walmart.com/seller/v1", "http://localhost:39055");
        Assert.AreEqual("http://localhost:39055/seller/v1", result);
    }

    [TestMethod]
    public void Override_PreservesTokenPath()
    {
        var result = WalmartBaseUrlOverride.Apply(
            WalletClientCredentialsTokenStrategy.DefaultTokenUrl, "https://gateway.internal:8443");
        Assert.AreEqual("https://gateway.internal:8443/seller/v1/auth/token", result);
    }

    [TestMethod]
    public void Override_WithoutPort_UsesSchemeDefault()
    {
        var result = WalmartBaseUrlOverride.Apply(
            "https://marketplace.walmart.com/seller", "https://example.com");
        Assert.AreEqual("https://example.com/seller", result);
    }
}
