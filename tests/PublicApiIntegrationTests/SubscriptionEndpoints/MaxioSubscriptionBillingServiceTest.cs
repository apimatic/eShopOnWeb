using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingServiceTest
{
    private static readonly BillingBuyer Buyer = new("shopper@example.com", "shopper@example.com");

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndConfirmsPlanPriceStateAndNextBilling()
    {
        using var host = new BillingTestHost();

        var result = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsTrue(result.Created);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual(29900, result.Subscription.PriceInCents);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.IsNotNull(result.Subscription.NextBillingAt);
        Assert.AreEqual(1, host.Maxio.CustomerCreates);
        Assert.AreEqual(1, host.Maxio.SubscriptionCreates);
        StringAssert.Contains(host.Maxio.LastSubscriptionCreateBody, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(host.Maxio.LastSubscriptionCreateBody, "\"product_handle\":\"eshop-pro\"");
    }

    [TestMethod]
    public async Task ConcurrentSubscribeForSamePlanReachesMaxioOnce()
    {
        using var host = new BillingTestHost();
        host.Maxio.CreateSubscriptionDelay = TimeSpan.FromMilliseconds(500);

        // A double-click: two requests, each with its own DbContext, racing for the same claim.
        var attempts = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => CaptureAsync(host.CreateService().SubscribeAsync(Buyer, "eshop-pro"))))
            .ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.AreEqual(1, host.Maxio.SubscriptionCreates, "exactly one create may reach Maxio");
        Assert.AreEqual(1, host.Maxio.CustomerCreates);
        Assert.AreEqual(1, outcomes.Count(o => o.Result?.Created == true));
        Assert.IsTrue(outcomes.All(o =>
            o.Result is not null || o.Error is BillingException { Kind: BillingErrorKind.InProgress }));
    }

    [TestMethod]
    public async Task RepeatSubscribeReturnsExistingSubscription()
    {
        using var host = new BillingTestHost();
        var first = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        var second = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsFalse(second.Created);
        Assert.AreEqual(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.AreEqual(1, host.Maxio.SubscriptionCreates);
    }

    [TestMethod]
    public async Task SubscribeAfterClaimStoreIsLostAdoptsExistingCustomerAndSubscription()
    {
        using var host = new BillingTestHost();
        await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        // A restart of the in-memory store: the claims are gone, Maxio still holds the records.
        using (var db = host.CreateDbContext())
        {
            db.SubscriptionClaims.RemoveRange(db.SubscriptionClaims);
            db.BillingCustomerClaims.RemoveRange(db.BillingCustomerClaims);
            await db.SaveChangesAsync();
        }

        var again = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsFalse(again.Created);
        Assert.AreEqual(1, host.Maxio.CustomerCreates);
        Assert.AreEqual(1, host.Maxio.SubscriptionCreates);
    }

    [TestMethod]
    public async Task SubscriptionCreateConnectionLostAfterMaxioActedIsSettledByReference()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextSubscriptionCreateFailure = "drop-after-apply";

        var result = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, host.Maxio.SubscriptionCreates, "the SDK must not resend the POST");
        Assert.AreEqual(BillingClaimState.Completed, (await SingleSubscriptionClaimAsync(host)).State);
    }

    [TestMethod]
    public async Task SubscriptionCreateUnreadableSuccessIsSettledByReference()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextSubscriptionCreateFailure = "garbage-after-apply";

        var result = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsTrue(result.Created);
        Assert.AreEqual(BillingClaimState.Completed, (await SingleSubscriptionClaimAsync(host)).State);
    }

    [TestMethod]
    public async Task SubscriptionCreateConnectionLostBeforeMaxioActedIsLeftUnknownThenSettledLater()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextSubscriptionCreateFailure = "drop-before-apply";

        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.AreEqual(BillingErrorKind.OutcomePending, error.Kind);
        Assert.AreEqual(BillingClaimState.Unknown, (await SingleSubscriptionClaimAsync(host)).State);

        // Shortly afterwards the outcome is still open: the caller sees it as pending, nothing is re-sent.
        var listed = await host.CreateService().GetSubscriptionsAsync(Buyer);
        CollectionAssert.Contains(listed.PendingPlanHandles.ToList(), "eshop-pro");
        await Assert.ThrowsExceptionAsync<BillingException>(() => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));
        Assert.AreEqual(1, host.Maxio.SubscriptionCreates);

        // Once the claim is stale and Maxio still has nothing for the reference, it is released and a retry succeeds.
        host.Clock.Advance(MaxioSubscriptionBillingService.ClaimStaleAfter + TimeSpan.FromSeconds(1));
        var retried = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsTrue(retried.Created);
        Assert.AreEqual(2, host.Maxio.SubscriptionCreates);
        Assert.AreEqual(1, host.Maxio.SubscriptionsByReference.Count);
    }

    [TestMethod]
    public async Task UnknownSubscriptionIsSettledByMySubscriptionsWhenMaxioHasIt()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextSubscriptionCreateFailure = "drop-before-apply";
        await Assert.ThrowsExceptionAsync<BillingException>(() => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        // Maxio did act after all (e.g. it was slow to show the subscription): simulate it appearing.
        var service = host.CreateService();
        host.Maxio.NextSubscriptionCreateFailure = null;
        var claim = await SingleSubscriptionClaimAsync(host);
        var customer = host.Maxio.CustomersByReference.Values.Single();
        await new HttpClientShim(host).CreateSubscriptionDirectlyAsync(claim.Reference, (int)customer["id"]!);

        var listed = await service.GetSubscriptionsAsync(Buyer);

        Assert.AreEqual(0, listed.PendingPlanHandles.Count);
        Assert.AreEqual(1, listed.Subscriptions.Count);
        Assert.AreEqual(BillingClaimState.Completed, (await SingleSubscriptionClaimAsync(host)).State);
    }

    [TestMethod]
    public async Task CustomerCreateConnectionLostAfterMaxioActedIsSettledByReference()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextCustomerCreateFailure = "drop-after-apply";

        var result = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, host.Maxio.CustomerCreates);
        Assert.AreEqual(1, host.Maxio.CustomersByReference.Count);
    }

    [TestMethod]
    public async Task CustomerCreateConnectionLostBeforeMaxioActedIsLeftUnknown()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextCustomerCreateFailure = "drop-before-apply";

        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.AreEqual(BillingErrorKind.OutcomePending, error.Kind);
        using var db = host.CreateDbContext();
        Assert.AreEqual(BillingClaimState.Unknown, (await db.BillingCustomerClaims.SingleAsync()).State);
        Assert.AreEqual(0, await db.SubscriptionClaims.CountAsync(), "the subscription claim is released when no subscription was attempted");
        Assert.AreEqual(0, host.Maxio.SubscriptionCreates);
    }

    [TestMethod]
    public async Task SubscriptionRejectionIsReportedWithProviderErrorsAndReleasesTheClaim()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextSubscriptionCreateFailure = "reject";

        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.AreEqual(BillingErrorKind.Rejected, error.Kind);
        CollectionAssert.Contains(error.Errors.ToList(), "No payment method was on file for the $299.00 balance");
        using (var db = host.CreateDbContext())
        {
            Assert.AreEqual(0, await db.SubscriptionClaims.CountAsync());
        }

        var retried = await host.CreateService().SubscribeAsync(Buyer, "eshop-pro");
        Assert.IsTrue(retried.Created);
    }

    [TestMethod]
    public async Task CustomerRejectionIsReportedWithProviderErrors()
    {
        using var host = new BillingTestHost();
        host.Maxio.NextCustomerCreateFailure = "reject";

        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));

        Assert.AreEqual(BillingErrorKind.Rejected, error.Kind);
        CollectionAssert.Contains(error.Errors.ToList(), "Email address: is invalid.");
        using var db = host.CreateDbContext();
        Assert.AreEqual(0, await db.BillingCustomerClaims.CountAsync());
        Assert.AreEqual(0, await db.SubscriptionClaims.CountAsync());
    }

    [TestMethod]
    public async Task PlanNotOfferedByConfiguredFamilyIsRefusedBeforeAnyWrite()
    {
        using var host = new BillingTestHost();

        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "api-call"));

        Assert.AreEqual(BillingErrorKind.InvalidRequest, error.Kind);
        Assert.AreEqual(0, host.Maxio.CustomerCreates);
        Assert.AreEqual(0, host.Maxio.SubscriptionCreates);
    }

    [TestMethod]
    public async Task PlanListStopsAtThePageCapAndSaysItIsIncomplete()
    {
        using var host = new BillingTestHost();
        host.Maxio.EndlessProductPages = true;

        var catalog = await host.CreateService().GetPlansAsync();

        Assert.IsFalse(catalog.IsComplete);
        Assert.AreEqual(MaxioSubscriptionBillingService.MaxPlanPages * MaxioSubscriptionBillingService.PlanPageSize, catalog.Plans.Count);
        Assert.AreEqual(MaxioSubscriptionBillingService.MaxPlanPages, host.Maxio.Requests.Count(r => r.EndsWith("/products.json")));

        // A plan missing from a cut-short list is not reported as invalid: the list could not prove it.
        var error = await Assert.ThrowsExceptionAsync<BillingException>(
            () => host.CreateService().SubscribeAsync(Buyer, "eshop-pro"));
        Assert.AreEqual(BillingErrorKind.CatalogIncomplete, error.Kind);
    }

    [TestMethod]
    public async Task PlanListStopsWhenPagingMakesNoProgress()
    {
        using var host = new BillingTestHost();
        host.Maxio.RepeatingProductPages = true;

        var catalog = await host.CreateService().GetPlansAsync();

        Assert.IsFalse(catalog.IsComplete);
        Assert.AreEqual(2, host.Maxio.Requests.Count(r => r.EndsWith("/products.json")));
    }

    [TestMethod]
    public async Task MySubscriptionsWithoutMaxioCustomerIsEmptyAndCreatesNothing()
    {
        using var host = new BillingTestHost();

        var listed = await host.CreateService().GetSubscriptionsAsync(Buyer);

        Assert.AreEqual(0, listed.Subscriptions.Count);
        Assert.AreEqual(0, host.Maxio.CustomerCreates);
    }

    private static async Task<SubscriptionClaim> SingleSubscriptionClaimAsync(BillingTestHost host)
    {
        using var db = host.CreateDbContext();
        return await db.SubscriptionClaims.AsNoTracking().SingleAsync();
    }

    private static async Task<(SubscribeResult? Result, Exception? Error)> CaptureAsync(Task<SubscribeResult> call)
    {
        try
        {
            return (await call, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>Creates a subscription at the fake directly, as if Maxio had acted on an earlier request.</summary>
    private sealed class HttpClientShim
    {
        private readonly BillingTestHost _host;

        public HttpClientShim(BillingTestHost host) => _host = host;

        public async Task CreateSubscriptionDirectlyAsync(string reference, int customerId)
        {
            using var client = new System.Net.Http.HttpClient(_host.Maxio, disposeHandler: false);
            var body = $"{{\"subscription\":{{\"product_handle\":\"eshop-pro\",\"customer_id\":{customerId},\"reference\":\"{reference}\"}}}}";
            using var response = await client.PostAsync(FakeMaxio.BaseUrl + "/subscriptions.json",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"), CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }
    }
}
