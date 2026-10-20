using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Host-level checks that never reach the billing provider (the host runs with placeholder Maxio settings).
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetEndpointsRequireAToken(string route)
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleIsABadRequest()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle");
    }

    [TestMethod]
    public async Task SwaggerDocumentListsTheSubscriptionRoutes()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(document, "/api/subscription-plans");
        StringAssert.Contains(document, "/api/subscriptions");
        StringAssert.Contains(document, "/api/my-subscriptions");
    }

    [TestMethod]
    public void HostRefusesToStartWithoutAnApiKey()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "",
                ["Maxio:Subdomain"] = "placeholder-site",
                ["Maxio:ProductFamilyHandle"] = "placeholder-family",
                ["Maxio:BaseUrl"] = "",
            })));

        var ex = Assert.ThrowsException<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }
}
