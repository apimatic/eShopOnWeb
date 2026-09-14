using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscription endpoints over real HTTP with the billing system stubbed out, so the
/// route, authentication, payload and status-code contract is covered without external calls.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> _application = null!;
    private static FakeSubscriptionService _billing = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _billing = new FakeSubscriptionService();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Keep the test deterministic regardless of any locally configured default plan.
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Maxio:DefaultPlanHandle"] = string.Empty }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionService>();
                services.AddSingleton<ISubscriptionService>(_billing);
            });
        });
    }

    [ClassCleanup]
    public static void ClassCleanup() => _application?.Dispose();

    private static HttpClient CreateClient(string? token)
    {
        var client = _application.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static HttpClient NormalUserClient => CreateClient(ApiTokenHelper.GetNormalUserToken());

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ListPlansRequiresABearerToken()
    {
        var response = await CreateClient(token: null).GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsTheBillingCatalog()
    {
        var response = await NormalUserClient.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>(JsonOptions);

        Assert.IsNotNull(model);
        CollectionAssert.AreEquivalent(
            new[] { "basic-plan", "eshop-pro" },
            model!.SubscriptionPlans.Select(plan => plan.Handle).ToArray());

        var pro = model.SubscriptionPlans.Single(plan => plan.Handle == "eshop-pro");
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await CreateClient(token: null).PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeCreatesOnceAndIsIdempotentAfterwards()
    {
        var client = CreateClient(ApiTokenHelper.GetAdminUserToken());

        var first = await client.PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstModel = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
        Assert.IsTrue(firstModel!.Created);
        Assert.AreEqual("eshop-pro", firstModel.Subscription!.PlanHandle);
        Assert.AreEqual("active", firstModel.Subscription.State);
        Assert.IsTrue(firstModel.Subscription.IsLive);
        Assert.IsNotNull(firstModel.Subscription.NextBillingAt);

        var second = await client.PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondModel = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
        Assert.IsFalse(secondModel!.Created);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription!.Id);
    }

    [TestMethod]
    public async Task SubscribeRejectsAMissingPlanHandle()
    {
        var response = await NormalUserClient.PostAsync("api/subscriptions", Json(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "planHandle");
    }

    [TestMethod]
    public async Task SubscribeReturnsNotFoundForAnUnknownPlan()
    {
        var response = await NormalUserClient.PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeSurfacesUpstreamFailuresAsBadGateway()
    {
        var response = await NormalUserClient.PostAsync(
            "api/subscriptions", Json(new { planHandle = FakeSubscriptionService.ProviderFailurePlanHandle }));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresABearerToken()
    {
        var response = await CreateClient(token: null).GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsOnlyTheCallersSubscriptions()
    {
        var subscribe = await NormalUserClient.PostAsync("api/subscriptions", Json(new { planHandle = "basic-plan" }));
        subscribe.EnsureSuccessStatusCode();
        var created = await subscribe.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);

        var mine = await NormalUserClient.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var model = await mine.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(JsonOptions);

        Assert.IsNotNull(model);
        CollectionAssert.Contains(
            model!.Subscriptions.Select(subscription => subscription.Id).ToArray(),
            created!.Subscription!.Id);
        Assert.IsTrue(model.ActiveSubscriptions.All(subscription => subscription.IsLive));
        Assert.IsTrue(model.Subscriptions.All(subscription => subscription.CustomerEmail == "demouser@microsoft.com"),
            "a shopper must only ever see their own subscriptions");
    }
}
