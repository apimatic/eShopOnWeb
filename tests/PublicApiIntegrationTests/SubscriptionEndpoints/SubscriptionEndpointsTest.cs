using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private sealed class StubGateway : IBillingGateway
    {
        private readonly Dictionary<string, SubscriptionInfo> _subs = new();
        public int CreateCalls;
        public Exception? ListFailure;

        public Task<PlanListResult> ListPlansAsync(CancellationToken ct)
        {
            if (ListFailure is not null) throw ListFailure;
            return Task.FromResult(new PlanListResult(new[] { new PlanInfo("eshop-pro", "Pro", null, 29900, 1, "month") }, false));
        }
        public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct) =>
            Task.FromResult<BillingCustomer?>(_subs.Count == 0 ? null : new BillingCustomer(1));
        public Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken ct) =>
            Task.FromResult(new BillingCustomer(1));
        public Task<SubscriptionInfo?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct) =>
            Task.FromResult(_subs.TryGetValue(reference, out var s) ? s : null);
        public Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
        {
            CreateCalls++;
            var info = new SubscriptionInfo(77, planHandle, "Pro", 29900, "USD", "active", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
            _subs[reference] = info;
            return Task.FromResult(info);
        }
        public Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>(_subs.Values.ToList());
    }

    private static (HttpClient Client, StubGateway Gateway) NewClient(string? token)
    {
        var gateway = new StubGateway();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IBillingGateway>();
                s.AddSingleton<IBillingGateway>(gateway);
            }));
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return (client, gateway);
    }

    [TestMethod]
    public async Task AllThreeEndpointsReturn401WithoutAToken()
    {
        var (client, _) = NewClient(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        var (client, _) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("eshop-pro", doc.RootElement.GetProperty("plans")[0].GetProperty("handle").GetString());
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndShowsUpInMySubscriptions()
    {
        var (client, gateway) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        var second = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        var mine = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        Assert.AreEqual(1, gateway.CreateCalls);
        using var doc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        var sub = doc.RootElement.GetProperty("subscriptions")[0];
        Assert.AreEqual("eshop-pro", sub.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task UnknownPlanIsRejectedWith400()
    {
        var (client, gateway) = NewClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "does-not-exist" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, gateway.CreateCalls);
    }

    [TestMethod]
    public async Task ProviderOutageIsMappedTo503WithoutLeakingDetails()
    {
        var (client, gateway) = NewClient(ApiTokenHelper.GetNormalUserToken());
        gateway.ListFailure = new BillingProviderException(BillingFailureKind.Unavailable, "internal detail https://secret.example");

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains("secret.example"));
    }
}
