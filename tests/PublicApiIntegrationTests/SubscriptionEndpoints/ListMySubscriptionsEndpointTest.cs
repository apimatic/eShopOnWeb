using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These tests exercise the real Maxio sandbox - see the note on CreateSubscriptionEndpointTest.
/// </summary>
[TestClass]
public class ListMySubscriptionsEndpointTest
{
    private static HttpClient AuthenticatedClient(string userName)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        return client;
    }

    private static string NewTestBuyer(string label) => $"itest-{label}-{Guid.NewGuid():N}@example.com";

    [TestMethod]
    public async Task ReturnsAnEmptyListForABuyerWhoHasNeverSubscribed()
    {
        var client = AuthenticatedClient(NewTestBuyer("never-subscribed"));

        var response = await client.GetAsync("/api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();

        Assert.IsNotNull(body);
        Assert.AreEqual(0, body!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReflectsASubscriptionJustCreated()
    {
        var client = AuthenticatedClient(NewTestBuyer("just-subscribed"));

        var createResponse = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync("/api/my-subscriptions");
        listResponse.EnsureSuccessStatusCode();
        var body = await listResponse.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();

        Assert.IsNotNull(body);
        Assert.IsTrue(body!.Subscriptions.Any(s => s.PlanHandle == "eshop-pro" && s.State == "active"));
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
