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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program>? _factory;
    private static FakeMaxioSubscriptionService _fake = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _fake = new FakeMaxioSubscriptionService();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMaxioSubscriptionService>();
                    services.AddSingleton<IMaxioSubscriptionService>(_fake);
                });
            });
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory?.Dispose();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _fake.Reset();
    }

    [TestMethod]
    public async Task Plans_RequireAuthentication()
    {
        var response = await Client().GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Plans_ReturnsCatalogForAuthenticatedUser()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/subscription-plans");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("plans").EnumerateArray().ToList();
        Assert.AreEqual(2, plans.Count);

        var pro = plans.Single(p => p.GetProperty("handle").GetString() == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.GetProperty("name").GetString());
        Assert.AreEqual(299m, pro.GetProperty("price").GetDecimal());
        Assert.IsFalse(pro.GetProperty("requiresPaymentMethod").GetBoolean());
    }

    [TestMethod]
    public async Task Subscribe_CreatesSubscription_AndIsIdempotent()
    {
        var client = Client();

        var first = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.Created, first.Status);
        Assert.IsTrue(first.Body.RootElement.GetProperty("created").GetBoolean());
        var firstId = first.Body.RootElement.GetProperty("subscription").GetProperty("id").GetInt64();
        Assert.AreEqual("active", first.Body.RootElement.GetProperty("subscription").GetProperty("state").GetString());
        Assert.AreEqual(299m, first.Body.RootElement.GetProperty("subscription").GetProperty("price").GetDecimal());

        var second = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.OK, second.Status);
        Assert.IsFalse(second.Body.RootElement.GetProperty("created").GetBoolean());
        Assert.AreEqual(firstId, second.Body.RootElement.GetProperty("subscription").GetProperty("id").GetInt64());

        // And the shopper's own list reflects exactly one current subscription to the plan.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/my-subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var listResponse = await client.SendAsync(request);
        listResponse.EnsureSuccessStatusCode();
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var mine = listDoc.RootElement.GetProperty("subscriptions").EnumerateArray().ToList();
        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual("eshop-pro", mine[0].GetProperty("planHandle").GetString());
        Assert.AreEqual(firstId, mine[0].GetProperty("id").GetInt64());
    }

    [TestMethod]
    public async Task MySubscriptions_IsEmpty_BeforeAnySubscription()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/my-subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, doc.RootElement.GetProperty("subscriptions").GetArrayLength());
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_ReturnsNotFound()
    {
        var result = await Subscribe(Client(), "no-such-plan");

        Assert.AreEqual(HttpStatusCode.NotFound, result.Status);
    }

    [TestMethod]
    public async Task Subscribe_MissingPlanHandle_ReturnsBadRequest()
    {
        var result = await Subscribe(Client(), "  ");

        Assert.AreEqual(HttpStatusCode.BadRequest, result.Status);
    }

    private static HttpClient Client() => _factory!.CreateClient();

    private static async Task<(HttpStatusCode Status, JsonDocument Body)> Subscribe(HttpClient client, string planHandle)
    {
        var payload = new { planHandle };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscriptions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, body);
    }
}

/// <summary>In-memory stand-in for the Maxio gateway so endpoint behaviour can be tested without a network.</summary>
internal sealed class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly List<MaxioPlan> _plans;
    private readonly Dictionary<string, List<MaxioSubscription>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 500_000;

    public FakeMaxioSubscriptionService()
    {
        _plans =
        [
            new MaxioPlan
            {
                Id = 1, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1,
                IntervalUnit = "month", RequiresCreditCard = false, Taxable = false
            },
            new MaxioPlan
            {
                Id = 2, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1,
                IntervalUnit = "month", RequiresCreditCard = false, Taxable = false
            }
        ];
    }

    public void Reset()
    {
        _subscriptions.Clear();
        _nextId = 500_000;
    }

    public Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioPlan> result = _plans.ToList();
        return Task.FromResult(result);
    }

    public Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MaxioCustomer { Id = 42, Reference = reference, Email = email });
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string reference, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioSubscription> result = Current(reference).ToList();
        return Task.FromResult(result);
    }

    public Task<SubscriptionEnrollmentResult> SubscribeAsync(string reference, string email, string planHandle, CancellationToken cancellationToken)
    {
        var plan = _plans.FirstOrDefault(p => p.Handle == planHandle);
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var existing = Current(reference).FirstOrDefault(s => s.PlanHandle == plan.Handle);
        if (existing is not null)
        {
            return Task.FromResult(new SubscriptionEnrollmentResult(existing, created: false));
        }

        var created = new MaxioSubscription
        {
            Id = _nextId++,
            State = "active",
            PlanHandle = plan.Handle,
            PlanName = plan.Name,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            Currency = "USD",
            CustomerId = 42,
            ActivatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            PaymentCollectionMethod = "remittance"
        };

        var list = _subscriptions.GetValueOrDefault(reference) ?? new List<MaxioSubscription>();
        list.Add(created);
        _subscriptions[reference] = list;

        return Task.FromResult(new SubscriptionEnrollmentResult(created, created: true));
    }

    private IEnumerable<MaxioSubscription> Current(string reference) =>
        _subscriptions.GetValueOrDefault(reference)?.Where(s => s.IsCurrent) ?? Enumerable.Empty<MaxioSubscription>();
}
