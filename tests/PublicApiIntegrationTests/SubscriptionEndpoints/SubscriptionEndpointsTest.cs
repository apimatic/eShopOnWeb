using System;
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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> CreateFactory(FakeMaxioClient fake)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioAdvancedBillingClient>();
                services.RemoveAll<MaxioSettings>();
                services.AddSingleton(new MaxioSettings
                {
                    ApiKey = "test-key",
                    Subdomain = "test-site",
                    ProductFamilyHandle = "eshop-subscribe"
                });
                services.AddSingleton<IMaxioAdvancedBillingClient>(fake);
            });
        });
    }

    private static HttpClient AuthedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task SubscriptionPlansRequiresAuthentication()
    {
        using var factory = CreateFactory(new FakeMaxioClient());
        var response = await factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlansReturnsTheFamilyPlans()
    {
        using var factory = CreateFactory(new FakeMaxioClient());
        var client = AuthedClient(factory);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("subscriptionPlans").EnumerateArray().ToList();

        Assert.AreEqual(2, plans.Count);
        Assert.IsTrue(plans.Any(p => p.GetProperty("handle").GetString() == "eshop-pro" && p.GetProperty("priceInCents").GetInt32() == 29900));
        Assert.IsTrue(plans.Any(p => p.GetProperty("handle").GetString() == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionAndReplaysItOnTheSecondRequest()
    {
        var fake = new FakeMaxioClient();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory);

        var first = await client.PostAsync("api/subscriptions", JsonContent("{\"planHandle\":\"eshop-pro\"}"));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.IsTrue(firstDoc.RootElement.GetProperty("created").GetBoolean());
        var sub = firstDoc.RootElement.GetProperty("subscription");
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.AreEqual(29900, sub.GetProperty("priceInCents").GetInt32());
        Assert.AreEqual("USD", sub.GetProperty("currency").GetString());
        Assert.IsNotNull(sub.GetProperty("nextBillingDateUtc").GetString());
        var subscriptionId = sub.GetProperty("maxioSubscriptionId").GetInt64();

        // Double-click: same deterministic enrollment reference -> the live subscription is replayed.
        var second = await client.PostAsync("api/subscriptions", JsonContent("{\"planHandle\":\"eshop-pro\"}"));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.IsFalse(secondDoc.RootElement.GetProperty("created").GetBoolean());
        Assert.AreEqual(subscriptionId, secondDoc.RootElement.GetProperty("subscription").GetProperty("maxioSubscriptionId").GetInt64());

        // And the provider saw exactly one customer create and one subscription create.
        Assert.AreEqual(1, fake.CustomerCreateAttempts);
        Assert.AreEqual(1, fake.SubscriptionCreateAttempts);
    }

    [TestMethod]
    public async Task SubscribeRejectsUnknownPlanAndMissingHandle()
    {
        var fake = new FakeMaxioClient();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory);

        var unknown = await client.PostAsync("api/subscriptions", JsonContent("{\"planHandle\":\"no-such-plan\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);

        var missing = await client.PostAsync("api/subscriptions", JsonContent("{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);

        Assert.AreEqual(0, fake.CustomerCreateAttempts);
        Assert.AreEqual(0, fake.SubscriptionCreateAttempts);
    }

    [TestMethod]
    public async Task MySubscriptionsListsTheUsersEnrollments()
    {
        var fake = new FakeMaxioClient();
        using var factory = CreateFactory(fake);
        var client = AuthedClient(factory);

        // No enrollments yet -> empty list.
        var emptyResponse = await client.GetAsync("api/my-subscriptions");
        emptyResponse.EnsureSuccessStatusCode();
        using (var emptyDoc = JsonDocument.Parse(await emptyResponse.Content.ReadAsStringAsync()))
        {
            Assert.AreEqual(0, emptyDoc.RootElement.GetProperty("subscriptions").GetArrayLength());
        }

        await client.PostAsync("api/subscriptions", JsonContent("{\"planHandle\":\"basic-plan\"}"));

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subs = doc.RootElement.GetProperty("subscriptions").EnumerateArray().ToList();

        Assert.AreEqual(1, subs.Count);
        Assert.AreEqual("basic-plan", subs[0].GetProperty("planHandle").GetString());
        Assert.AreEqual("active", subs[0].GetProperty("state").GetString());
    }

    private static StringContent JsonContent(string json) => new StringContent(json, Encoding.UTF8, "application/json");
}

/// <summary>
/// In-memory double of the Maxio Advanced Billing client honouring reference-uniqueness,
/// so endpoint tests don't need a live sandbox.
/// </summary>
public class FakeMaxioClient : IMaxioAdvancedBillingClient
{
    public const string UserId = "demouser@microsoft.com";

    private readonly Dictionary<string, MaxioCustomer> _customers = new();
    private readonly Dictionary<string, MaxioSubscription> _subscriptions = new();
    private long _nextId = 900001;

    public int CustomerCreateAttempts { get; private set; }
    public int SubscriptionCreateAttempts { get; private set; }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(string familyHandle, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioPlan> plans = new List<MaxioPlan>
        {
            new(1, "basic-plan", "Basic Plan", null, 2900, 1, "month", false, familyHandle, "eShopSubscribe", null),
            new(2, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false, familyHandle, "eShopSubscribe", null)
        };
        return Task.FromResult(plans);
    }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => Task.FromResult(_customers.TryGetValue(reference, out var customer) ? customer : null);

    public Task<MaxioCustomer> EnsureCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        if (!_customers.TryGetValue(reference, out var customer))
        {
            CustomerCreateAttempts++;
            customer = new MaxioCustomer(700001 + CustomerCreateAttempts, reference, firstName, lastName, email);
            _customers[reference] = customer;
        }
        return Task.FromResult(customer);
    }

    public Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        => Task.FromResult(_subscriptions.TryGetValue(reference, out var subscription) ? subscription : null);

    public Task<MaxioSubscription?> SubscribeAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken = default)
    {
        SubscriptionCreateAttempts++;
        var plan = planHandle == "eshop-pro"
            ? ("Pro Plan", 29900)
            : ("Basic Plan", 2900);

        var subscription = new MaxioSubscription(
            _nextId++,
            reference,
            "active",
            customerId,
            0,
            planHandle,
            plan.Item1,
            plan.Item2,
            "USD",
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow.AddMonths(1),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            "remittance");

        _subscriptions[reference] = subscription;
        return Task.FromResult<MaxioSubscription?>(subscription);
    }

    public Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MaxioSubscription> subs = _subscriptions.Values.Where(s => s.CustomerId == customerId).ToList();
        return Task.FromResult(subs);
    }
}
