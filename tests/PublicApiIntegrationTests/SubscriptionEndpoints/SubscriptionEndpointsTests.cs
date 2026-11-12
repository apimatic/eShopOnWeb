using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    [TestMethod]
    public async Task PostSubscriptionsRequiresJwt()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions", new { planHandle = "x" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresJwt()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow(null, "sub", "family", null)]
    [DataRow("  ", "sub", "family", null)]
    [DataRow("key", "", "family", null)]
    [DataRow("key", "sub", null, null)]
    [DataRow("key", "sub", "family", "not-a-url")]
    [DataRow("key", "sub", "family", "ftp://host")]
    public void SettingsValidationFailsFast(string? apiKey, string? subdomain, string? family, string? baseUrl)
    {
        var settings = new MaxioSettings
        {
            ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family, BaseUrl = baseUrl
        };
        Assert.ThrowsException<InvalidOperationException>(settings.Validate);
    }

    [TestMethod]
    public void SettingsValidationAcceptsCompleteSettingsAndOptionalBaseUrl()
    {
        new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f" }.Validate();
        new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", BaseUrl = "https://mock.local:1234" }.Validate();
    }

    [TestMethod]
    public void SettingsErrorsNameTheKeyNotTheValue()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            new MaxioSettings { ApiKey = " ", Subdomain = "s", ProductFamilyHandle = "f" }.Validate);
        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }
}
