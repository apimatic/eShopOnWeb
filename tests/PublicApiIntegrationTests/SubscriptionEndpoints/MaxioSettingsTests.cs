using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSettingsTests
{
    private static bool Valid(MaxioSettings s) => new MaxioSettingsValidator().Validate(null, s).Succeeded;

    [TestMethod]
    public void RefusesMissingApiKey() =>
        Assert.IsFalse(Valid(new MaxioSettings { Subdomain = "s", ProductFamilyHandle = "f" }));

    [TestMethod]
    public void RefusesBlankFamilyHandle() =>
        Assert.IsFalse(Valid(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = " " }));

    [TestMethod]
    public void SubdomainRequiredUnlessBaseUrlSet()
    {
        Assert.IsFalse(Valid(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" }));
        Assert.IsTrue(Valid(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://example.test" }));
    }

    [TestMethod]
    public void RefusesNonAbsoluteBaseUrl() =>
        Assert.IsFalse(Valid(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "not a url" }));
}
