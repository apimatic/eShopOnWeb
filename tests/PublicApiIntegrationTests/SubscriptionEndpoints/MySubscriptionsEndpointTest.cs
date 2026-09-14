using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MySubscriptionsEndpointTest
{
    private static SubscriptionApiFactory _factory = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new SubscriptionApiFactory();

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsEmptyListForUserWhoNeverSubscribed()
    {
        var client = AuthenticatedClientFor($"{Guid.NewGuid():N}@example.com");
        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReflectsAPreviouslyCreatedSubscription()
    {
        var userName = $"{Guid.NewGuid():N}@example.com";
        var client = AuthenticatedClientFor(userName);

        var subscribeJson = JsonSerializer.Serialize(new { planHandle = "eshop-pro" });
        var subscribeResponse = await client.PostAsync("api/subscriptions",
            new StringContent(subscribeJson, Encoding.UTF8, "application/json"));
        subscribeResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
    }

    private HttpClient AuthenticatedClientFor(string userName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenForUser(userName));
        return client;
    }
}
