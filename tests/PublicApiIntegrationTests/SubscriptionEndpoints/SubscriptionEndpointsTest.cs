using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient AuthenticatedClient(SubscriptionEndpointsWebApplicationFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task SubscriptionPlans_RequiresAuthentication()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        var response = await factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlans_ReturnsPlansFromMaxio()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        factory.FakeMaxio.Plans.Add(new SubscriptionPlan
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            RequiresPaymentMethod = false
        });

        var client = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(1, model!.Plans.Count);
        var plan = model.Plans[0];
        Assert.AreEqual("eshop-pro", plan.Handle);
        Assert.AreEqual(299.00m, plan.Price);
        Assert.IsFalse(plan.RequiresPaymentMethod);
    }

    [TestMethod]
    public async Task CreateSubscription_RequiresAuthentication()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        var response = await factory.CreateClient().PostAsync("api/subscriptions", JsonBody(new { productHandle = "eshop-pro" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_EnrollsCallerIdentifiedFromTheirToken_NotFromTheRequestBody()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        factory.FakeMaxio.SubscribeResult = new SubscriptionEnrollment
        {
            MaxioSubscriptionId = 8001,
            MaxioCustomerId = 501,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            State = "active",
            PriceInCents = 29900,
            NextAssessmentAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)
        };

        var client = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/subscriptions", JsonBody(new { productHandle = "eshop-pro" }));
        response.EnsureSuccessStatusCode();

        // The buyer id Maxio sees comes from the validated JWT claim, never from the JSON body.
        Assert.AreEqual("demouser@microsoft.com", factory.FakeMaxio.LastSubscribeBuyerId);
        Assert.AreEqual("eshop-pro", factory.FakeMaxio.LastSubscribeProductHandle);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual(8001, model!.Subscription.MaxioSubscriptionId);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), model.Subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsBadRequest_WhenProductHandleIsMissing()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        var client = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { productHandle = "" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, factory.FakeMaxio.SubscribeCallCount);
    }

    [TestMethod]
    public async Task MySubscriptions_RequiresAuthentication()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        var response = await factory.CreateClient().GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsOnlyTheCallingBuyersSubscriptions()
    {
        using var factory = new SubscriptionEndpointsWebApplicationFactory();
        factory.FakeMaxio.Subscriptions.Add(new SubscriptionEnrollment
        {
            MaxioSubscriptionId = 8001,
            MaxioCustomerId = 501,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            State = "active",
            PriceInCents = 29900
        });

        var client = AuthenticatedClient(factory, ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        Assert.AreEqual("demouser@microsoft.com", factory.FakeMaxio.LastGetSubscriptionsBuyerId);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].ProductHandle);
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
