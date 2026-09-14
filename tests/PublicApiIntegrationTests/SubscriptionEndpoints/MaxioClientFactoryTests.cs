using System;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioClientFactoryTests
{
    [TestMethod]
    public void UsesBaseUrlOverrideVerbatim()
    {
        const string baseUrl = "https://billing-proxy.example.test/root/";
        var options = MaxioClientFactory.CreateOptions(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "ignored-site",
            ProductFamilyHandle = "test-family",
            BaseUrl = baseUrl
        });

        Assert.AreEqual(ServerEnvironment.Us, options.Environment);
        Assert.AreEqual(baseUrl, options.Server.Production.Us.BaseUrl);
        Assert.AreEqual("test-key", options.BasicAuth?.Username);
    }

    [TestMethod]
    public void UsesSubdomainWhenBaseUrlIsAbsent()
    {
        var options = MaxioClientFactory.CreateOptions(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "sandbox-site",
            ProductFamilyHandle = "test-family"
        });

        Assert.AreEqual("sandbox-site", options.Server.Production.Us.Site);
        StringAssert.Contains(options.Server.Production.Us.BaseUrl, "{site}");
    }

    [TestMethod]
    public void RejectsMissingCredentialsAndCatalog()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions());

        Assert.IsFalse(result.Succeeded);
    }
}
