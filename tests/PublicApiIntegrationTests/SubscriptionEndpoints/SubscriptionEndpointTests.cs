using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private FakeMaxioBillingClient _maxio = null!;

    [TestInitialize]
    public void Initialize()
    {
        _maxio = new FakeMaxioBillingClient();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioBillingClient>();
                services.AddSingleton<IMaxioBillingClient>(_maxio);
            });
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task SubscriptionRoutesRequireBearerToken()
    {
        using var client = _factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task HeroFlowCreatesOneCustomerAndOneSubscriptionForConcurrentRequests()
    {
        using var client = AuthenticatedClient();

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<List<BillingPlan>>();
        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual(29900L, plans.Single(x => x.Handle == "eshop-pro").PriceInCents);

        var first = client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var second = client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var responses = await Task.WhenAll(first, second);

        Assert.IsTrue(responses.All(x => x.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, _maxio.CustomerCreateCount);
        Assert.AreEqual(1, _maxio.SubscriptionCreateCount);

        var subscriptionsResponse = await client.GetAsync("api/my-subscriptions");
        subscriptionsResponse.EnsureSuccessStatusCode();
        var subscriptions = await subscriptionsResponse.Content.ReadFromJsonAsync<List<SubscriptionDetails>>();
        Assert.IsNotNull(subscriptions);
        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", subscriptions[0].ProductHandle);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.AreEqual(29900L, subscriptions[0].PriceInCents);
        Assert.IsNotNull(subscriptions[0].NextBillingAt);
    }

    [TestMethod]
    public async Task SubscribeRejectsProductOutsideConfiguredFamily()
    {
        using var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "api/subscriptions",
            new { productHandle = "not-in-family" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, _maxio.SubscriptionCreateCount);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private sealed class FakeMaxioBillingClient : IMaxioBillingClient
    {
        private readonly ConcurrentDictionary<string, BillingCustomer> _customers = new();
        private readonly ConcurrentDictionary<string, BillingSubscription> _subscriptions = new();
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<BillingPlan> plans = new[]
            {
                new BillingPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false),
                new BillingPlan("basic-plan", "Basic Plan", null, 2900, 1, "month", false)
            };
            return Task.FromResult(plans);
        }

        public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            _customers.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }

        public Task<BillingCustomer> CreateCustomerAsync(
            string reference,
            string firstName,
            string lastName,
            string email,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _customerCreateCount);
            var customer = new BillingCustomer(41, reference, email);
            _customers.TryAdd(reference, customer);
            return Task.FromResult(_customers[reference]);
        }

        public Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(
            long customerId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<BillingSubscription> result = _subscriptions.Values
                .Where(x => x.CustomerId == customerId)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<BillingSubscription?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<BillingSubscription> CreateSubscriptionAsync(
            long customerId,
            string productHandle,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _subscriptionCreateCount);
            await Task.Delay(75, cancellationToken);
            var customer = _customers.Values.Single(x => x.Id == customerId);
            var price = productHandle == "eshop-pro" ? 29900 : 2900;
            var subscription = new BillingSubscription(
                71,
                subscriptionReference,
                "active",
                price,
                DateTimeOffset.UtcNow.AddMonths(1),
                DateTimeOffset.UtcNow.AddMonths(1),
                customerId,
                customer.Reference,
                productHandle,
                productHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
                1,
                "month",
                "integration-test-family");
            _subscriptions.TryAdd(subscriptionReference, subscription);
            return _subscriptions[subscriptionReference];
        }
    }
}
