using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CreateSubscriptionRequest = Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.CreateSubscriptionRequest;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static SubscriptionTestApplication CreateApplication()
    {
        var app = new SubscriptionTestApplication();
        app.FakeMaxioClient.Products.Add(new MaxioProductDto
        {
            Id = 1,
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        });
        app.FakeMaxioClient.Products.Add(new MaxioProductDto
        {
            Id = 2,
            Handle = "basic-plan",
            Name = "Basic Plan",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month"
        });
        return app;
    }

private static HttpRequestMessage AuthorizedGet(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return request;
    }

    private static HttpRequestMessage AuthorizedPost(string url, string token, CreateSubscriptionRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };
        request.Headers.Add("Authorization", $"Bearer {token}");
        returnturn request;
    }

    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsPlans()
    {
        using var app = CreateApplication();
        var client = app.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var response = await client.SendAsync(AuthorizedGet("/api/subscription-plans", token));

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<SubscriptionPlanListResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Plans.Count);
        Assert.AreEqual("basic-plan", model.Plans[0].Handle);
        Assert.AreEqual(29m, model.Plans[0].Price);
        Assert.AreEqual("eshop-pro", model.Plans[1].Handle);
        Assert.AreEqual(299m, model.Plans[1].Price);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsCreatedSubscription()
    {
        using var app = CreateApplication();
        var client = app.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var request = new CreateSubscriptionRequest { PlanHandle = "eshop-pro" };
        var response = await client.PostAsJsonAsync("/api/subscriptions", request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual("eshop-pro", model!.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.PlanName);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsTrue(model.Subscription.IsNew);
        Assert.AreEqual(1, app.FakeMaxioClient.CreateCustomerCalls);
        Assert.AreEqual(1, app.FakeMaxioClient.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task CreateSubscription_IsIdempotent_OnDoubleClick()
    {
        using var app = CreateApplication();
        var client = app.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var request = new CreateSubscriptionRequest { PlanHandle = "eshop-pro" };
        var first = await client.PostAsJsonAsync("/api/subscriptions", request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        first.EnsureSuccessStatusCode();
        var firstModel = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        var second = await client.PostAsJsonAsync("/api/subscriptions", request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        second.EnsureSuccessStatusCode();
        var secondModel = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        Assert.AreEqual(firstModel!.Subscription.SubscriptionId, secondModel!.Subscription.SubscriptionId);
        Assert.IsTrue(firstModel.Subscription.IsNew);
        Assert.IsFalse(secondModel.Subscription.IsNew);
        Assert.AreEqual(1, app.FakeMaxioClient.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsSubscriptions()
    {
        using var app = CreateApplication();
        var client = app.CreateClient();
        var token = ApiTokenHelper.GetNormalUserToken();

        var subscribe = new CreateSubscriptionRequest { PlanHandle = "basic-plan" };
        var subscribeResponse = await client.PostAsJsonAsync("/api/subscriptions", subscribe, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        subscribeResponse.EnsureSuccessStatusCode();

        var response = await client.SendAsync(AuthorizedGet("/api/my-subscriptions", token));
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("basic-plan", model.Subscriptions[0].PlanHandle);
        Assert.AreEqual("Basic Plan", model.Subscriptions[0].PlanName);
        Assert.AreEqual(29m, model.Subscriptions[0].Price);
    }

    [TestMethod]
    public async Task Endpoints_RequireAuthentication()
    {
        using var app = CreateApplication();
        var client = app.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var mine = await client.GetAsync("/api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync("/api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }
}
