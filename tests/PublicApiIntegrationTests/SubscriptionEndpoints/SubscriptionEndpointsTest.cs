using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// End-to-end checks of the subscription endpoints through the real host: routing, JWT
/// authorization, identity resolution from the token, and the billing-failure to status-code map.
/// The billing provider is stubbed so these run without credentials or network.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<ISubscriptionBillingService, StubSubscriptionBillingService>()));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory?.Dispose();

    private static HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ListPlansRequiresABearerToken()
    {
        var response = await _factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await _factory.CreateClient().PostAsync("api/subscriptions", Json("{}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresABearerToken()
    {
        var response = await _factory.CreateClient().GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsTheCatalog()
    {
        var response = await AuthenticatedClient().GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(1, model.Plans.Count);
        Assert.AreEqual(StubSubscriptionBillingService.KnownPlanHandle, model.Plans[0].Handle);
        Assert.AreEqual(299m, model.Plans[0].Price);
        Assert.AreEqual("every month", model.Plans[0].BillingPeriod);
    }

    [TestMethod]
    public async Task SubscribeThenRepeatIsIdempotentAndShowsUpInMySubscriptions()
    {
        var client = AuthenticatedClient();
        var body = Json($$"""{"planHandle":"{{StubSubscriptionBillingService.KnownPlanHandle}}"}""");

        var first = await client.PostAsync("api/subscriptions", body);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        var firstModel = await first.Content.ReadFromJsonAsync<SubscribeResponse>();
        Assert.IsNotNull(firstModel?.Subscription);
        Assert.IsTrue(firstModel.Created);
        Assert.AreEqual("active", firstModel.Subscription.State);
        Assert.IsNotNull(firstModel.Subscription.NextBillingAt);

        var second = await client.PostAsync(
            "api/subscriptions",
            Json($$"""{"planHandle":"{{StubSubscriptionBillingService.KnownPlanHandle}}"}"""));

        // A repeated request is absorbed: 200 rather than 201, same subscription, created false.
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondModel = await second.Content.ReadFromJsonAsync<SubscribeResponse>();
        Assert.IsNotNull(secondModel?.Subscription);
        Assert.IsFalse(secondModel.Created);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = await mine.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();

        Assert.IsNotNull(mineModel);
        Assert.AreEqual(1, mineModel.Subscriptions.Count);
        Assert.AreEqual(firstModel.Subscription.Id, mineModel.Subscriptions[0].Id);
        Assert.IsTrue(mineModel.Subscriptions[0].IsActive);
    }

    [TestMethod]
    public async Task HandlesConcurrentRequestsFromTheSameShopper()
    {
        // Regression guard: endpoint instances are resolved once at startup, so constructor-injecting
        // a scoped, DbContext-backed service makes every request share one DbContext. That surfaces
        // only under concurrency, as "a second operation was started on this context instance".
        var client = AuthenticatedClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            client.PostAsync(
                "api/subscriptions",
                Json($$"""{"planHandle":"{{StubSubscriptionBillingService.KnownPlanHandle}}"}"""))));

        foreach (var response in responses)
        {
            Assert.IsTrue(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"expected 200 or 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        // At most one of the twelve may report a new enrollment, and every response must name the
        // same subscription. (Whether the one creation happens here depends on which test ran first;
        // the stub's state is shared across the class.)
        var created = 0;
        var ids = new HashSet<long>();
        foreach (var response in responses)
        {
            var model = await response.Content.ReadFromJsonAsync<SubscribeResponse>();
            ids.Add(model!.Subscription!.Id);
            if (model.Created)
            {
                created++;
            }
        }

        Assert.IsTrue(created <= 1, $"{created} of the concurrent requests claimed to have created an enrollment");
        Assert.AreEqual(1, ids.Count, "concurrent requests resolved to more than one subscription");
    }

    [TestMethod]
    public async Task ConcurrentMySubscriptionsRequestsAllSucceed()
    {
        var client = AuthenticatedClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => client.GetAsync("api/my-subscriptions")));

        foreach (var response in responses)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }

    [TestMethod]
    public async Task SubscribeToAnUnknownPlanReturnsNotFound()
    {
        var response = await AuthenticatedClient()
            .PostAsync("api/subscriptions", Json("""{"planHandle":"no-such-plan"}"""));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task BillingSystemFailureIsReportedAsServiceUnavailable()
    {
        var response = await AuthenticatedClient().PostAsync(
            "api/subscriptions",
            Json($$"""{"planHandle":"{{StubSubscriptionBillingService.UnavailablePlanHandle}}"}"""));

        // A provider outage is a 503 the caller can retry, not a 500 and not a 4xx.
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
