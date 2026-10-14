using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSettingsValidatorTest
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family",
    };

    [TestMethod]
    public void AcceptsCompleteSettings()
    {
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void RejectsMissingOrBlankApiKey_NamingTheKey(string? apiKey)
    {
        var settings = Valid();
        settings.ApiKey = apiKey;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "Maxio:ApiKey");
    }

    [TestMethod]
    public void RejectsMissingProductFamilyHandle()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = " ";

        StringAssert.Contains(new MaxioSettingsValidator().Validate(null, settings).FailureMessage, "Maxio:ProductFamilyHandle");
    }

    [TestMethod]
    public void SubdomainIsOptionalWhenBaseUrlIsSet()
    {
        var settings = Valid();
        settings.Subdomain = null;
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, settings).Failed);

        settings.BaseUrl = "https://billing-proxy.example.com";
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, settings).Succeeded);
    }

    [TestMethod]
    [DataRow("not a url")]
    [DataRow("ftp://example.com")]
    public void RejectsInvalidBaseUrl(string baseUrl)
    {
        var settings = Valid();
        settings.BaseUrl = baseUrl;

        StringAssert.Contains(new MaxioSettingsValidator().Validate(null, settings).FailureMessage, "Maxio:BaseUrl");
    }

    [TestMethod]
    public void BaseUrlOverride_IsUsedVerbatim_OnTheSelectedEnvironment()
    {
        var settings = Valid();
        settings.BaseUrl = "https://billing-proxy.example.com/maxio";

        var us = MaxioBillingServiceCollectionExtensions.BuildClientOptions(settings, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        Assert.AreEqual("https://billing-proxy.example.com/maxio", us.Server.Production.Us.BaseUrl);

        settings.Environment = "EU";
        var eu = MaxioBillingServiceCollectionExtensions.BuildClientOptions(settings, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        Assert.AreEqual("https://billing-proxy.example.com/maxio", eu.Server.Production.Eu.BaseUrl);
    }

    [TestMethod]
    public void WithoutBaseUrl_SubdomainFillsTheSiteVariable()
    {
        var options = MaxioBillingServiceCollectionExtensions.BuildClientOptions(Valid(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.AreEqual("test-site", options.Server.Production.Us.Site);
        Assert.AreEqual("x", options.BasicAuth!.Password);
        Assert.IsFalse(options.Logging.LogRequestBody);
        Assert.IsNotNull(options.Logging.LoggerFactory);
    }
}
