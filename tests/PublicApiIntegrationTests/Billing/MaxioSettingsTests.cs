using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

[TestClass]
public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "placeholder-key",
        Subdomain = "placeholder-site",
        ProductFamilyHandle = "placeholder-family"
    };

    [TestMethod]
    public void Validator_AcceptsCompleteSettings()
    {
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [TestMethod]
    public void Validator_RejectsEachBlankRequiredKey_NamingTheKeyButNoValue()
    {
        var cases = new (MaxioSettings Settings, string Key)[]
        {
            (Clone(Valid(), apiKey: " "), "Maxio:ApiKey"),
            (Clone(Valid(), subdomain: ""), "Maxio:Subdomain"),
            (Clone(Valid(), family: null), "Maxio:ProductFamilyHandle"),
        };

        foreach (var (settings, key) in cases)
        {
            var result = new MaxioSettingsValidator().Validate(null, settings);
            Assert.IsTrue(result.Failed, key);
            StringAssert.Contains(result.FailureMessage, key);
            Assert.IsFalse(result.FailureMessage.Contains("placeholder"), "values are never echoed");
        }
    }

    [TestMethod]
    public void Validator_BaseUrlOverrideReplacesSubdomain_AndMustBeAnAbsoluteHttpUrl()
    {
        var withOverride = Clone(Valid(), subdomain: "");
        withOverride.BaseUrl = "http://127.0.0.1:5005";
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, withOverride).Succeeded);

        withOverride.BaseUrl = "not a url";
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, withOverride).Failed);
    }

    [TestMethod]
    public void Validator_RejectsASubdomainThatIsAHostName()
    {
        Assert.IsTrue(new MaxioSettingsValidator().Validate(null, Clone(Valid(), subdomain: "evil.example.com/x")).Failed);
    }

    [TestMethod]
    public void ClientOptions_UseBaseUrlVerbatim_AndBasicAuthWithTheApiKey()
    {
        var settings = Valid();
        settings.BaseUrl = "https://billing-proxy.example.test";

        var options = MaxioConfigurationExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance, System.TimeProvider.System);

        Assert.AreEqual("https://billing-proxy.example.test", options.Server.Production.Us.BaseUrl);
        Assert.AreEqual("placeholder-site", options.Server.Production.Us.Site);
        Assert.AreEqual("placeholder-key", options.BasicAuth!.Username);
        Assert.AreEqual("x", options.BasicAuth.Password);
        Assert.IsFalse(options.Logging.LogRequestBody);
        Assert.IsNotNull(options.Logging.LoggerFactory);
        Assert.IsFalse(options.Retry.HttpMethodsToRetry.Any(m => m.Method == "POST"), "the SDK must never resend a POST");
    }

    [TestMethod]
    public void EnvironmentFallback_FillsOnlyBlankKeys()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "from-config",
            ["Maxio:Subdomain"] = ""
        });
        var environment = new Dictionary<string, string?>
        {
            ["MAXIO_API_KEY"] = "from-env-key",
            ["MAXIO_SITE_SUBDOMAIN"] = "from-env-site",
            ["MAXIO_DEFAULT_PRODUCT_FAMILY"] = "from-env-family"
        };

        builder.AddMaxioEnvironmentFallback(builder.Build(), name => environment.GetValueOrDefault(name));
        var configuration = builder.Build();

        Assert.AreEqual("from-config", configuration["Maxio:ApiKey"]);
        Assert.AreEqual("from-env-site", configuration["Maxio:Subdomain"]);
        Assert.AreEqual("from-env-family", configuration["Maxio:ProductFamilyHandle"]);
        Assert.IsNull(configuration["Maxio:BaseUrl"]);
    }

    private static MaxioSettings Clone(MaxioSettings source, string? apiKey = "keep", string? subdomain = "keep", string? family = "keep") => new()
    {
        ApiKey = apiKey == "keep" ? source.ApiKey : apiKey,
        Subdomain = subdomain == "keep" ? source.Subdomain : subdomain,
        ProductFamilyHandle = family == "keep" ? source.ProductFamilyHandle : family,
        BaseUrl = source.BaseUrl
    };
}
