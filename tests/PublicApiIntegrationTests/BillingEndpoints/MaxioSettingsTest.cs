using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

[TestClass]
public class MaxioSettingsTest
{
    [TestMethod]
    public void BlankCredentialsAreRefused()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = " ", Subdomain = "" });
        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "Maxio:ApiKey");
        StringAssert.Contains(result.FailureMessage, "Maxio:Subdomain");
    }

    [TestMethod]
    public void UnknownEnvironmentIsRefused()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "MARS" });
        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "Maxio:Environment");
    }

    [TestMethod]
    public void BaseUrlOverrideIsUsedVerbatimForTheSelectedEnvironment()
    {
        var options = MaxioServiceCollectionExtensions.BuildOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "eu", BaseUrl = "https://example.test/Custom/" });
        Assert.AreEqual("https://example.test/Custom/", options.Server.Production.Eu.BaseUrl);

        options = MaxioServiceCollectionExtensions.BuildOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "US" });
        Assert.AreEqual("https://{site}.chargify.com", options.Server.Production.Us.BaseUrl);
        Assert.AreEqual("s", options.Server.Production.Us.Site);
    }
}
