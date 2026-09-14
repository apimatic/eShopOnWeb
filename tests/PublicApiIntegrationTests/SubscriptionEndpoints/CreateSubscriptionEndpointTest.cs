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
public class CreateSubscriptionEndpointTest
{
    private static SubscriptionApiFactory _factory = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new SubscriptionApiFactory();

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("api/subscriptions", GetRequestBody("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForUnknownPlanHandle()
    {
        var client = AuthenticatedClientFor($"{Guid.NewGuid():N}@example.com");
        var response = await client.PostAsync("api/subscriptions", GetRequestBody("no-such-plan"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingReturnsPlanDetails()
    {
        var client = AuthenticatedClientFor($"{Guid.NewGuid():N}@example.com");
        var response = await client.PostAsync("api/subscriptions", GetRequestBody("eshop-pro"));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var model = body.FromJson<CreateSubscriptionResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual("eshop-pro", model!.Subscription.PlanHandle);
        Assert.AreEqual(29900, model.Subscription.PriceInCents);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsNotNull(model.Subscription.NextAssessmentAt);
    }

    [TestMethod]
    public async Task DoubleClickDoesNotCreateASecondSubscription()
    {
        var userName = $"{Guid.NewGuid():N}@example.com";
        var client = AuthenticatedClientFor(userName);

        var first = await client.PostAsync("api/subscriptions", GetRequestBody("basic-plan"));
        var second = await client.PostAsync("api/subscriptions", GetRequestBody("basic-plan"));
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(firstModel!.Subscription.SubscriptionId, secondModel!.Subscription.SubscriptionId);

        var mySubscriptions = await client.GetAsync("api/my-subscriptions");
        var mySubscriptionsModel = (await mySubscriptions.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.AreEqual(1, mySubscriptionsModel!.Subscriptions.Count);
    }

    private HttpClient AuthenticatedClientFor(string userName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenForUser(userName));
        return client;
    }

    private static StringContent GetRequestBody(string planHandle)
    {
        var json = JsonSerializer.Serialize(new { planHandle });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
