using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingServiceTests
{
    private static Task<SubscribeResult> Subscribe(BillingTestHost host, string plan = "pro") =>
        host.InRequestAsync(billing => billing.SubscribeAsync(BillingTestHost.UserName, plan, "Test", "Shopper"));

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndConfirmsPlanPriceStateNextBilling()
    {
        await using var host = await BillingTestHost.CreateAsync();

        var result = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual("pro", result.Subscription!.PlanHandle);
        Assert.AreEqual(29900, result.Subscription.PriceInCents);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        var createCustomer = host.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/customers.json").ParseBody()!;
        Assert.AreEqual($"eshop-user-{host.UserId}", createCustomer["customer"]!["reference"]!.GetValue<string>());
        Assert.AreEqual(BillingTestHost.UserName, createCustomer["customer"]!["email"]!.GetValue<string>());

        var createSubscription = host.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json").ParseBody()!;
        Assert.AreEqual("pro", createSubscription["subscription"]!["product_handle"]!.GetValue<string>());
        Assert.AreEqual($"eshop-sub-{host.UserId}-{FakeMaxio.FamilyHandle}", createSubscription["subscription"]!["reference"]!.GetValue<string>());
        Assert.AreEqual("remittance", createSubscription["subscription"]!["payment_collection_method"]!.GetValue<string>());

        var enrollment = await host.GetEnrollmentAsync();
        Assert.AreEqual(EnrollmentState.Active, enrollment!.State);
        Assert.AreEqual(result.Subscription.SubscriptionId, enrollment.BillingSubscriptionId);
        Assert.IsTrue((await host.GetBillingCustomerAsync())!.IsProvisioned);
    }

    [TestMethod]
    public async Task Subscribe_Repeated_ReturnsExistingWithoutSecondCreate()
    {
        await using var host = await BillingTestHost.CreateAsync();

        var first = await Subscribe(host);
        var second = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.AreEqual(first.Subscription!.SubscriptionId, second.Subscription!.SubscriptionId);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task Subscribe_DoubleClick_OnlyOneCreateReachesMaxio()
    {
        await using var host = await BillingTestHost.CreateAsync();
        var firstCreateEntered = new TaskCompletionSource();
        var releaseFirstCreate = new TaskCompletionSource();
        host.Maxio.BeforeCreateSubscription = async () =>
        {
            firstCreateEntered.TrySetResult();
            await releaseFirstCreate.Task;
        };

        var first = Subscribe(host);
        await firstCreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await Subscribe(host);          // arrives while the first create is in flight
        releaseFirstCreate.SetResult();

        Assert.AreEqual(SubscribeOutcome.Pending, second.Outcome);
        Assert.AreEqual(SubscribeOutcome.Created, (await first).Outcome);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, host.Maxio.SubscriptionCount);
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentBurst_CreatesExactlyOneCustomerAndSubscription()
    {
        await using var host = await BillingTestHost.CreateAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Subscribe(host))));

        Assert.AreEqual(1, results.Count(r => r.Outcome == SubscribeOutcome.Created));
        Assert.IsTrue(results.All(r => r.Outcome is SubscribeOutcome.Created or SubscribeOutcome.Pending or SubscribeOutcome.AlreadySubscribed));
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task Subscribe_DifferentPlanWhileSubscribed_ReturnsConflict()
    {
        await using var host = await BillingTestHost.CreateAsync();
        await Subscribe(host, "pro");

        var result = await Subscribe(host, "basic");

        Assert.AreEqual(SubscribeOutcome.SubscribedToOtherPlan, result.Outcome);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_AfterSubscriptionCanceledInMaxio_AllowsNewSubscription()
    {
        await using var host = await BillingTestHost.CreateAsync();
        var first = await Subscribe(host, "pro");
        host.Maxio.SetSubscriptionState(first.Subscription!.SubscriptionId, "canceled");

        var second = await Subscribe(host, "basic");

        Assert.AreEqual(SubscribeOutcome.Created, second.Outcome);
        Assert.AreEqual(2, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [TestMethod]
    public async Task Subscribe_PlanNotOfferedByFamily_IsRejectedBeforeAnyWrite()
    {
        await using var host = await BillingTestHost.CreateAsync();

        var result = await Subscribe(host, "api-call");

        Assert.AreEqual(SubscribeOutcome.UnknownPlan, result.Outcome);
        Assert.AreEqual(0, host.Maxio.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.IsNull(await host.GetEnrollmentAsync());
    }

    [TestMethod]
    public async Task CreateSubscription_Rejected422_ReleasesClaimAndReturnsErrors()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.BeforeRoute = r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json"
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("No payment method was on file") })
            : null;

        var result = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Rejected, result.Outcome);
        CollectionAssert.Contains(result.Errors!.ToList(), "No payment method was on file");
        Assert.IsNull(await host.GetEnrollmentAsync(), "a refused create must release the claim so the user can retry");
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionDrops_SettlesFromFindSubscription()
    {
        await using var host = await BillingTestHost.CreateAsync();
        // Maxio creates the subscription, then the connection dies before the response arrives.
        host.Maxio.AfterRoute = r =>
        {
            if (r.Method == HttpMethod.Post && r.Path == "/subscriptions.json")
                throw new HttpRequestException("connection reset");
        };

        var result = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"), "a POST is never resent");
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Get, "/subscriptions/lookup.json"));
        var enrollment = await host.GetEnrollmentAsync();
        Assert.AreEqual(EnrollmentState.Active, enrollment!.State);
        Assert.AreEqual(result.Subscription!.SubscriptionId, enrollment.BillingSubscriptionId);
    }

    [TestMethod]
    public async Task CreateSubscription_ConnectionDropsAndLookupMisses_StaysUnknownUntilSettled()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.SubscriptionLookupFindsCreated = false;
        host.Maxio.AfterRoute = r =>
        {
            if (r.Method == HttpMethod.Post && r.Path == "/subscriptions.json")
                throw new HttpRequestException("connection reset");
        };

        var result = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Pending, result.Outcome, "an unknown outcome is not reported as a failure");
        Assert.AreEqual(EnrollmentState.Unknown, (await host.GetEnrollmentAsync())!.State);

        // A retry while the outcome is unknown must not create a second subscription.
        host.Maxio.AfterRoute = null;
        Assert.AreEqual(SubscribeOutcome.Pending, (await Subscribe(host)).Outcome);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        // Once Maxio shows it, the reconciliation pass settles the enrollment.
        host.Maxio.SubscriptionLookupFindsCreated = true;
        var settled = await host.InScopeAsync(sp =>
            sp.GetRequiredService<MaxioSubscriptionBillingService>().SettleUnsettledEnrollmentsAsync(null, default));
        Assert.AreEqual(1, settled);
        Assert.AreEqual(EnrollmentState.Active, (await host.GetEnrollmentAsync())!.State);
    }

    [TestMethod]
    public async Task CreateSubscription_UnknownAndNeverLanded_ReleasedAfterGracePeriod()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.BeforeRoute = r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json"
            ? throw new HttpRequestException("connection refused")
            : null;

        Assert.AreEqual(SubscribeOutcome.Pending, (await Subscribe(host)).Outcome);

        host.Maxio.BeforeRoute = null;
        host.Clock.Advance(MaxioSubscriptionBillingServiceTestsAccess.UnknownGracePeriod + TimeSpan.FromSeconds(1));
        var retry = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
        Assert.AreEqual(1, host.Maxio.SubscriptionCount);
    }

    [TestMethod]
    public async Task CreateCustomer_ConnectionDrops_SettlesFromReadCustomerByReference()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.AfterRoute = r =>
        {
            if (r.Method == HttpMethod.Post && r.Path == "/customers.json")
                throw new HttpRequestException("connection reset");
        };

        var result = await Subscribe(host);

        Assert.AreEqual(SubscribeOutcome.Created, result.Outcome);
        Assert.AreEqual(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, host.Maxio.CustomerCount);
        Assert.AreEqual(2, host.Maxio.Count(HttpMethod.Get, "/customers/lookup.json"), "lookup before create, then re-read after the drop");
        Assert.IsTrue((await host.GetBillingCustomerAsync())!.IsProvisioned);
    }

    [TestMethod]
    public async Task CreateCustomer_Unreachable_ReleasesClaimsAndReportsUnavailable()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.BeforeRoute = r => r.Method == HttpMethod.Post && r.Path == "/customers.json"
            ? throw new HttpRequestException("connection refused")
            : null;

        var ex = await Assert.ThrowsExceptionAsync<BillingProviderException>(() => Subscribe(host));

        Assert.AreEqual(503, ex.StatusCode);
        Assert.IsNull(await host.GetEnrollmentAsync());
        Assert.IsNull(await host.GetBillingCustomerAsync());
        Assert.AreEqual(0, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task MaxioAuthFailure_IsReportedAsBadGatewayNotUnauthorized()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.BeforeRoute = _ => FakeMaxio.Empty(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsExceptionAsync<BillingProviderException>(() =>
            host.InRequestAsync(billing => billing.GetPlansAsync()));

        Assert.AreEqual(502, ex.StatusCode);
        Assert.AreEqual(401, ex.ProviderStatusCode);
    }

    [TestMethod]
    public async Task GetPlans_StopsAtPageCap_AndReportsTruncation()
    {
        await using var host = await BillingTestHost.CreateAsync();
        host.Maxio.Products.Clear();
        for (var i = 0; i < 1001; i++)
        {
            host.Maxio.Products.Add(FakeMaxio.Product(10_000 + i, $"plan-{i}", $"Plan {i}", 100));
        }

        var plans = await host.InRequestAsync(billing => billing.GetPlansAsync());

        Assert.IsTrue(plans.IsTruncated);
        Assert.AreEqual(1000, plans.Plans.Count);
        Assert.AreEqual(5, host.Maxio.Requests.Count(r => r.Path.EndsWith("/products.json")));
    }

    [TestMethod]
    public async Task GetPlans_ShortPage_IsComplete()
    {
        await using var host = await BillingTestHost.CreateAsync();

        var plans = await host.InRequestAsync(billing => billing.GetPlansAsync());

        Assert.IsFalse(plans.IsTruncated);
        CollectionAssert.AreEquivalent(new[] { "pro", "basic" }, plans.Plans.Select(p => p.Handle).ToList());
        StringAssert.Contains(host.Maxio.Requests.Single().Query, "per_page=200");
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReadsLiveFromMaxio()
    {
        await using var host = await BillingTestHost.CreateAsync();
        var none = await host.InRequestAsync(billing => billing.GetMySubscriptionsAsync(BillingTestHost.UserName));
        Assert.AreEqual(0, none.Subscriptions.Count);

        var created = await Subscribe(host);
        var mine = await host.InRequestAsync(billing => billing.GetMySubscriptionsAsync(BillingTestHost.UserName));

        Assert.AreEqual(created.Subscription!.SubscriptionId, mine.Subscriptions.Single().SubscriptionId);
        Assert.AreEqual("Pro Plan", mine.Subscriptions.Single().PlanName);
    }
}

internal static class MaxioSubscriptionBillingServiceTestsAccess
{
    public static readonly TimeSpan UnknownGracePeriod = TimeSpan.FromMinutes(5);
}
