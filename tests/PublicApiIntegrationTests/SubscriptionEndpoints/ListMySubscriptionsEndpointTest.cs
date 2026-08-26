using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListMySubscriptionsEndpointTest
{
    [TestMethod]
    public async Task ReturnsEmptyWhenUserHasNoBillingCustomer()
    {
        var handler = MaxioTestServer.ForSubscribeFlow();
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(0, model.Subscriptions.Count);

        // The customer lookup 404'd, so no subscriptions call was made.
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Get, "/customers/lookup.json"));
        Assert.AreEqual(0, handler.CountRequests(HttpMethod.Get, "/customers/501/subscriptions.json"));
    }

    [TestMethod]
    public async Task ReturnsExistingSubscriptions()
    {
        var handler = MaxioTestServer.ForSubscribeFlow();
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var subscribe = await client.PostAsync("api/subscriptions",
            new StringContent("{\"productHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json"));
        subscribe.EnsureSuccessStatusCode();

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model.Subscriptions.Count);
        Assert.AreEqual(9001, model.Subscriptions[0].Id);
        Assert.AreEqual("active", model.Subscriptions[0].State);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].ProductHandle);
        Assert.AreEqual(299.00m, model.Subscriptions[0].Price);
        Assert.IsNotNull(model.Subscriptions[0].NextBillingDate);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = MaxioTestServer.CreateFactory(MaxioTestServer.ForSubscribeFlow());
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
