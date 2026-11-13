using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioConfigurationTest
{
    [TestMethod]
    public async Task BaseUrlOverrideIsUsedVerbatimInsteadOfTheSubdomain()
    {
        var maxio = new FakeMaxioHandler();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Maxio:BaseUrl", "https://billing-proxy.example.test/maxio");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => maxio));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var uri = maxio.Uris.Single();
        Assert.AreEqual("billing-proxy.example.test", uri.Host);
        StringAssert.StartsWith(uri.AbsolutePath, "/maxio/product_families/");
    }

    [TestMethod]
    public void ValidatorRequiresTheCredentialFamilyAndASite()
    {
        var validator = new MaxioSettingsValidator();

        var missing = validator.Validate(null, new MaxioSettings());
        Assert.IsTrue(missing.Failed);
        StringAssert.Contains(missing.FailureMessage, "Maxio:ApiKey");
        StringAssert.Contains(missing.FailureMessage, "Maxio:ProductFamilyHandle");
        StringAssert.Contains(missing.FailureMessage, "Maxio:Subdomain");

        var blankKey = validator.Validate(null, new MaxioSettings { ApiKey = "  ", Subdomain = "site", ProductFamilyHandle = "family" });
        Assert.IsTrue(blankKey.Failed, "a blank key is not a configured key");

        Assert.IsTrue(validator.Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "family" }).Succeeded);
        Assert.IsTrue(validator.Validate(null, new MaxioSettings { ApiKey = "k", BaseUrl = "https://proxy.example.test", ProductFamilyHandle = "family" }).Succeeded,
            "a BaseUrl override replaces the subdomain");
        Assert.IsTrue(validator.Validate(null, new MaxioSettings { ApiKey = "k", BaseUrl = "not a url", ProductFamilyHandle = "family" }).Failed);
        Assert.IsTrue(validator.Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", Environment = "MARS" }).Failed);
        Assert.IsTrue(validator.Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", PaymentCollectionMethod = "cash" }).Failed);
    }
}
