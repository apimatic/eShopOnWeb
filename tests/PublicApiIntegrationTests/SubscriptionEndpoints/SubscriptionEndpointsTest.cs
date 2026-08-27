using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private FakeSubscriptionBillingService _billing = null!;

    [TestInitialize]
    public void Initialize()
    {
        _billing = new FakeSubscriptionBillingService();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(_billing);
            });
        });
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task SubscriptionPlansRequireJwt()
    {
        var response = await _client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperCanListPlansAndSubscribe()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await _client.GetAsync("/api/subscription-plans");
        var plansJson = await plansResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.AreEqual("eshop-pro", plansJson.GetProperty("plans")[0].GetProperty("handle").GetString());

        var subscribeResponse = await _client.PostAsJsonAsync(
            "/api/subscriptions",
            new SubscribeRequest { ProductHandle = "eshop-pro" });
        var subscribeJson = await subscribeResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(HttpStatusCode.Created, subscribeResponse.StatusCode);
        Assert.AreEqual("succeeded", subscribeJson.GetProperty("status").GetString());
        Assert.AreEqual("active", subscribeJson.GetProperty("subscription").GetProperty("state").GetString());
        Assert.AreEqual("demouser@microsoft.com", _billing.LastUser?.Email);

        var mineResponse = await _client.GetAsync("/api/my-subscriptions");
        var mineJson = await mineResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(HttpStatusCode.OK, mineResponse.StatusCode);
        Assert.AreEqual(1, mineJson.GetProperty("subscriptions").GetArrayLength());
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionSummary Subscription = new(
            "eshop-s-test",
            "eshop-pro",
            "Pro Plan",
            29900,
            "USD",
            "active",
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));

        public SubscriptionUser? LastUser { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[]
            {
                new SubscriptionPlan("eshop-pro", "Pro Plan", "Pro", 29900, 1, "month", false)
            });

        public Task<SubscribeResult> SubscribeAsync(
            SubscriptionUser user,
            string productHandle,
            CancellationToken cancellationToken)
        {
            LastUser = user;
            return Task.FromResult(SubscribeResult.Completed(Subscription));
        }

        public Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionSummary>>(new[] { Subscription });
    }
}
