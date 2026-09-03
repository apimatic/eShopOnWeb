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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task PlansRequireJwtAndReturnProviderPlans()
    {
        var service = new FakeBillingService();
        using var client = ProgramTest.CreateClient(service);

        var unauthorized = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Authorization = BearerToken();
        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plan = json.RootElement.GetProperty("plans")[0];
        Assert.AreEqual("basic-plan", plan.GetProperty("handle").GetString());
        Assert.AreEqual(2900, plan.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(29m, plan.GetProperty("price").GetDecimal());
    }

    [TestMethod]
    public async Task SubscribeUsesOnlyTheJwtIdentityAndReturnsConfirmation()
    {
        var service = new FakeBillingService();
        using var client = ProgramTest.CreateClient(service);
        client.DefaultRequestHeaders.Authorization = BearerToken();

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("eshop-pro", service.LastPlanHandle);
        Assert.AreEqual("demouser@microsoft.com", service.LastUser?.Email);
        Assert.IsFalse(string.IsNullOrWhiteSpace(service.LastUser?.Id));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var subscription = json.RootElement.GetProperty("subscription");
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual(29900, subscription.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("2026-10-03T00:00:00+00:00",
            subscription.GetProperty("nextBillingAt").GetDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsTheAuthenticatedUsersSubscriptions()
    {
        var service = new FakeBillingService();
        using var client = ProgramTest.CreateClient(service);
        client.DefaultRequestHeaders.Authorization = BearerToken();

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        Assert.AreEqual("demouser@microsoft.com", service.LastUser?.Email);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, json.RootElement.GetProperty("subscriptions").GetArrayLength());
    }

    private static AuthenticationHeaderValue BearerToken() =>
        new("Bearer", ApiTokenHelper.GetNormalUserToken());

    private sealed class FakeBillingService : ISubscriptionBillingService
    {
        private static readonly SubscriptionDetails Subscription = new(
            42,
            "opaque-reference",
            "eshop-pro",
            "Pro Plan",
            29900,
            "USD",
            "active",
            new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero),
            101,
            "default",
            "Default");

        public BillingUser? LastUser { get; private set; }

        public string? LastPlanHandle { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<SubscriptionPlan> plans =
            [
                new SubscriptionPlan(
                    "basic-plan",
                    "Basic Plan",
                    "Basic subscription",
                    2900,
                    1,
                    "month",
                    false)
            ];
            return Task.FromResult(plans);
        }

        public Task<SubscriptionDetails> SubscribeAsync(
            BillingUser user,
            string planHandle,
            CancellationToken cancellationToken)
        {
            LastUser = user;
            LastPlanHandle = planHandle;
            return Task.FromResult(Subscription with
            {
                PlanHandle = planHandle,
                PriceInCents = planHandle == "eshop-pro" ? 29900 : 2900
            });
        }

        public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
            BillingUser user,
            CancellationToken cancellationToken)
        {
            LastUser = user;
            IReadOnlyList<SubscriptionDetails> subscriptions = [Subscription];
            return Task.FromResult(subscriptions);
        }
    }
}
