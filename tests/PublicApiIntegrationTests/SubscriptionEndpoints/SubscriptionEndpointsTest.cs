using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private FakeBillingGateway _gateway = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _gateway = new FakeBillingGateway();
        _factory = ProgramTest.CreateFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISubscriptionBillingGateway>(_gateway)));
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task RequiresAJwt(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        var response = await AuthorizedClient().GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        StringAssert.Contains(body, "\"handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"price\":299");
        StringAssert.Contains(body, "\"isTruncated\":false");
    }

    [TestMethod]
    public async Task UnknownPlanIsABadRequest()
    {
        var response = await AuthorizedClient().PostAsJsonAsync("api/subscriptions", new { planHandle = "gold-plan" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _gateway.SubscriptionsCreated);
    }

    [TestMethod]
    public async Task DoubleClickCreatesExactlyOneCustomerAndOneSubscription()
    {
        var client = AuthorizedClient();
        _gateway.CreateDelay = TimeSpan.FromMilliseconds(300); // keep the first request in flight

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" }),
            client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" }));
        var third = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
        CollectionAssert.AreEqual(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);
        Assert.AreEqual(HttpStatusCode.OK, third.StatusCode); // already subscribed: same subscription returned
        Assert.AreEqual(1, _gateway.CustomersCreated);
        Assert.AreEqual(1, _gateway.SubscriptionsCreated);

        var mine = await client.GetStringAsync("api/my-subscriptions");
        StringAssert.Contains(mine, "\"planHandle\":\"eshop-pro\"");
        StringAssert.Contains(mine, "\"state\":\"active\"");
    }

    private sealed class FakeBillingGateway : ISubscriptionBillingGateway
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, BillingCustomer> _customers = new();
        private readonly List<BillingSubscription> _subscriptions = new();
        private int _customersCreated;
        private int _subscriptionsCreated;

        public TimeSpan CreateDelay { get; set; }
        public int CustomersCreated => _customersCreated;
        public int SubscriptionsCreated => _subscriptionsCreated;

        public Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BillingPlanCatalog("test-family", new[]
            {
                new BillingPlan(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false),
                new BillingPlan(2, "basic-plan", "Basic Plan", null, 2900, 1, "month", false)
            }, false));

        public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_gate) return Task.FromResult(_customers.TryGetValue(reference, out var c) ? c : null);
        }

        public Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var created = new BillingCustomer(Interlocked.Increment(ref _customersCreated), customer.Reference, customer.Email);
                _customers.Add(customer.Reference, created); // throws on a duplicate reference, like the provider
                return Task.FromResult(created);
            }
        }

        public Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
        {
            lock (_gate) return Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscriptions.Where(s => s.CustomerId == customerId).ToList());
        }

        public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken cancellationToken)
        {
            await Task.Delay(CreateDelay, cancellationToken);
            lock (_gate)
            {
                var id = 100 + Interlocked.Increment(ref _subscriptionsCreated);
                var subscription = new BillingSubscription(id, reference, customerId, "active", true, planHandle, "Pro Plan", 29900, "USD",
                    1, "month", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                _subscriptions.Add(subscription);
                return subscription;
            }
        }

        public Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            lock (_gate) return Task.FromResult(_subscriptions.FirstOrDefault(s => s.Reference == reference));
        }
    }
}
