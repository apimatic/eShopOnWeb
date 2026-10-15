using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static FakeBillingGateway _gateway = null!;
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _gateway = new FakeBillingGateway();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingGateway>();
                services.AddSingleton<ISubscriptionBillingGateway>(_gateway);
            }));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestMethod]
    public async Task Plans_RequireAToken()
    {
        var response = await _factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Plans_ListTheConfiguredFamily()
    {
        var response = await AuthorizedClient().GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var handles = json.RootElement.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, handles);
        Assert.IsFalse(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    public async Task Subscribe_ThenReplay_CreatesOnce_AndShowsInMySubscriptions()
    {
        var client = AuthorizedClient();

        var first = await client.PostAsync("api/subscriptions", Body(new { planHandle = "eshop-pro" }));
        var second = await client.PostAsync("api/subscriptions", Body(new { planHandle = "eshop-pro" }));
        var mine = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var created = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.AreEqual("created", created.RootElement.GetProperty("status").GetString());
        var subscription = created.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscription.GetProperty("planHandle").GetString());
        Assert.AreEqual(299m, subscription.GetProperty("price").GetDecimal());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.IsTrue(subscription.TryGetProperty("nextBillingAt", out var next) && next.ValueKind == JsonValueKind.String);

        Assert.AreEqual(1, _gateway.SubscriptionCreates.Count(s => s.PlanHandle == "eshop-pro"));
        Assert.AreEqual(1, _gateway.CustomerCreates.Count);

        mine.EnsureSuccessStatusCode();
        using var mineJson = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        Assert.IsTrue(mineJson.RootElement.GetProperty("subscriptions").EnumerateArray()
            .Any(s => s.GetProperty("planHandle").GetString() == "eshop-pro"));
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_IsBadRequest()
    {
        var response = await AuthorizedClient().PostAsync("api/subscriptions", Body(new { planHandle = "not-offered" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_MissingPlan_IsBadRequest()
    {
        var response = await AuthorizedClient().PostAsync("api/subscriptions", Body(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// <summary>In-process stand-in for Maxio, so endpoint tests never reach the network.</summary>
    private sealed class FakeBillingGateway : ISubscriptionBillingGateway
    {
        private readonly ConcurrentDictionary<string, BillingCustomerRecord> _customers = new();
        private readonly ConcurrentDictionary<string, (int CustomerId, BillingSubscription Subscription)> _subscriptions = new();
        private int _nextId = 1000;

        public ConcurrentBag<NewBillingCustomer> CustomerCreates { get; } = new();
        public ConcurrentBag<NewBillingSubscription> SubscriptionCreates { get; } = new();

        public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionPlanCatalog(new[]
            {
                new SubscriptionPlan(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month"),
                new SubscriptionPlan(2, "basic-plan", "Basic Plan", null, 2900, 1, "month")
            }, false));

        public Task<BillingCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_customers.TryGetValue(reference, out var customer) ? customer : null);

        public Task<BillingCustomerRecord> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
        {
            CustomerCreates.Add(customer);
            return Task.FromResult(_customers.GetOrAdd(customer.Reference, r => new BillingCustomerRecord(Interlocked.Increment(ref _nextId), r)));
        }

        public Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
        {
            SubscriptionCreates.Add(subscription);
            var plan = subscription.PlanHandle == "eshop-pro" ? ("Pro Plan", 29900L) : ("Basic Plan", 2900L);
            var created = new BillingSubscription(Interlocked.Increment(ref _nextId), subscription.Reference, "active", subscription.PlanHandle,
                plan.Item1, plan.Item2, "USD", 1, "month", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _subscriptions[subscription.Reference] = (subscription.BillingCustomerId, created);
            return Task.FromResult(created);
        }

        public Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_subscriptions.TryGetValue(reference, out var entry) ? entry.Subscription : null);

        public Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscriptions.Values
                .Where(e => e.CustomerId == billingCustomerId).Select(e => e.Subscription).ToList());
    }
}
