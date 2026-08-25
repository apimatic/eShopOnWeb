using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _application = null!;
    private FakeSubscriptionBillingService _billing = null!;

    [TestInitialize]
    public void Initialize()
    {
        _billing = new FakeSubscriptionBillingService();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(_billing);
            }));
    }

    [TestCleanup]
    public void Cleanup() => _application.Dispose();

    [TestMethod]
    public async Task PlansRequireJwtAndReturnBillingCatalog()
    {
        var unauthorized = await _application.CreateClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var client = CreateAuthenticatedClient();
        var plans = await client.GetFromJsonAsync<SubscriptionPlan[]>("api/subscription-plans");

        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans.Length);
        Assert.AreEqual("pro-test", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
    }

    [TestMethod]
    public async Task SubscribeUsesAuthenticatedIdentityAndOnlyAcceptsProductHandle()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "api/subscriptions",
            new SubscribeRequest("pro-test"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var subscription = await response.Content.ReadFromJsonAsync<SubscriptionDetails>();
        Assert.IsNotNull(subscription);
        Assert.AreEqual("pro-test", subscription.PlanHandle);
        Assert.IsNotNull(_billing.LastCustomer);
        Assert.AreEqual("demouser@microsoft.com", _billing.LastCustomer.Email);
        Assert.IsFalse(string.IsNullOrWhiteSpace(_billing.LastCustomer.UserId));
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsOnlyTheAuthenticatedUsersRecords()
    {
        var client = CreateAuthenticatedClient();

        var subscriptions = await client.GetFromJsonAsync<SubscriptionDetails[]>("api/my-subscriptions");

        Assert.IsNotNull(subscriptions);
        Assert.AreEqual(1, subscriptions.Length);
        Assert.IsFalse(string.IsNullOrWhiteSpace(_billing.LastListedUserId));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private sealed class FakeSubscriptionBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDetails Subscription = new(
            42,
            "eshop-sub-test",
            "pro-test",
            "Pro Plan",
            29900,
            "USD",
            "active",
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"));

        public BillingCustomer? LastCustomer { get; private set; }
        public string? LastListedUserId { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[]
            {
                new SubscriptionPlan("pro-test", "Pro Plan", null, 29900, 1, "month", false),
                new SubscriptionPlan("basic-test", "Basic Plan", null, 2900, 1, "month", false)
            });

        public Task<SubscriptionDetails> SubscribeAsync(
            BillingCustomer customer,
            string productHandle,
            CancellationToken cancellationToken)
        {
            LastCustomer = customer;
            return Task.FromResult(Subscription with { PlanHandle = productHandle });
        }

        public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            LastListedUserId = userId;
            return Task.FromResult<IReadOnlyList<SubscriptionDetails>>(new[] { Subscription });
        }
    }
}
