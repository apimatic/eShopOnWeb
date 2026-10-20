using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

/// <summary>
/// The CreateSubscription <c>payment_collection_method</c> follows the site's invoicing architecture, read with
/// Sites.ReadSite before any claim or write and cached for the process lifetime (successful reads only).
/// </summary>
[TestClass]
public class SiteCollectionMethodTests
{
    private const string SubscriptionsPath = "/subscriptions.json";

    private static string SentCollectionMethod(BillingTestHarness harness) =>
        FakeMaxio.CreatedSubscriptionCollectionMethod(
            harness.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath).Body)!;

    private static int SiteReads(BillingTestHarness harness) => harness.Maxio.Count(HttpMethod.Get, FakeMaxio.SitePath);

    private static async Task AssertNoClaimAndNoWrite(BillingTestHarness harness)
    {
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Method == HttpMethod.Post), "no write may reach Maxio");
        Assert.IsFalse(harness.Maxio.Requests.Any(r => r.Path == "/customers/lookup.json"), "the customer step is never reached");
        using var db = harness.NewContext();
        Assert.AreEqual(0, await db.BillingCustomerLinks.CountAsync());
        Assert.AreEqual(0, await db.SubscriptionEnrollments.CountAsync());
    }

    [TestMethod]
    public async Task RelationshipInvoicingSite_SendsRemittance()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.RelationshipInvoicingEnabled = true;

        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual("remittance", SentCollectionMethod(harness));
        var post = harness.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath);
        StringAssert.Contains(post.Body, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task LegacyStatementsSite_SendsInvoice()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.RelationshipInvoicingEnabled = false;

        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual("invoice", SentCollectionMethod(harness));
        var post = harness.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath);
        StringAssert.Contains(post.Body, "\"payment_collection_method\":\"invoice\"");
    }

    [TestMethod]
    public async Task SiteReadIsAfterThePlanCheck_AndBeforeTheCustomerStep()
    {
        var harness = new BillingTestHarness();

        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        var paths = harness.Maxio.Requests.Select(r => r.Path).ToList();
        var site = paths.IndexOf(FakeMaxio.SitePath);
        Assert.IsTrue(site > paths.IndexOf($"/product_families/handle:{FakeMaxio.FamilyHandle}/products.json"));
        Assert.IsTrue(site < paths.IndexOf("/customers/lookup.json"));
    }

    [TestMethod]
    public async Task NullRelationshipInvoicingFlag_Returns502_WithNoClaimAndNoWrite_AndIsNotCached()
    {
        var harness = new BillingTestHarness();
        harness.Maxio.RelationshipInvoicingEnabled = null;

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(502, ex.StatusCode);
        await AssertNoClaimAndNoWrite(harness);
        Assert.IsNull(harness.SiteCache.Cached, "an indeterminate architecture is never cached");

        // Not cached: the next subscribe reads the site again and, once the flag is present, proceeds.
        harness.Maxio.RelationshipInvoicingEnabled = false;
        var retry = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
        Assert.AreEqual(2, SiteReads(harness));
        Assert.AreEqual("invoice", SentCollectionMethod(harness));
    }

    [TestMethod]
    public async Task SiteReadFailure_Returns502_WithNoClaimAndNoWrite_AndIsNotCached()
    {
        var harness = new BillingTestHarness();
        var siteDown = true;
        harness.Maxio.Intercept = (request, _) => Task.FromResult(
            siteDown && request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == FakeMaxio.SitePath
                // 401 is not retried by the SDK, so the read fails fast.
                ? FakeMaxio.Json(HttpStatusCode.Unauthorized, new JsonObject())
                : null);

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default));

        Assert.AreEqual(502, ex.StatusCode);
        await AssertNoClaimAndNoWrite(harness);
        Assert.IsNull(harness.SiteCache.Cached, "a failed read is never cached");

        siteDown = false;
        var retry = await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);

        Assert.AreEqual(SubscribeOutcome.Created, retry.Outcome);
        Assert.AreEqual(2, SiteReads(harness), "the second subscribe re-reads the site");
        Assert.AreEqual("remittance", SentCollectionMethod(harness));
    }

    [TestMethod]
    public async Task SiteUnreachable_Returns503_WithNoClaimAndNoWrite()
    {
        var harness = new BillingTestHarness();
        var siteCalls = 0;
        harness.Maxio.Intercept = (request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == FakeMaxio.SitePath)
            {
                siteCalls++;
                throw BillingTestHarness.ConnectionReset();
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(
            () => BillingTestHarness.WithTimeout(harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default)));

        Assert.AreEqual(503, ex.StatusCode);
        Assert.IsTrue(siteCalls >= 1);
        await AssertNoClaimAndNoWrite(harness);
        Assert.IsNull(harness.SiteCache.Cached);
    }

    [TestMethod]
    public async Task SuccessfulRead_IsCached_SoLaterSubscribesDoNotReadTheSiteAgain()
    {
        var harness = new BillingTestHarness();

        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "pro-test", default);
        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller(), "basic-test", default);
        await harness.NewService().SubscribeAsync(BillingTestHarness.Caller("other@example.test"), "pro-test", default);

        Assert.AreEqual(1, SiteReads(harness));
        Assert.AreEqual(3, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.IsTrue(harness.Maxio.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath)
            .All(r => FakeMaxio.CreatedSubscriptionCollectionMethod(r.Body) == "remittance"));
    }

    [TestMethod]
    public async Task ConcurrentFirstSubscribes_ShareOneSiteRead()
    {
        var harness = new BillingTestHarness();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Maxio.Intercept = async (request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == FakeMaxio.SitePath)
            {
                arrived.TrySetResult();
                await release.Task;
            }

            return null;
        };

        var first = Task.Run(() => harness.NewService().SubscribeAsync(BillingTestHarness.Caller("a@example.test"), "pro-test", default));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = Task.Run(() => harness.NewService().SubscribeAsync(BillingTestHarness.Caller("b@example.test"), "pro-test", default));
        await Task.Delay(200);
        release.SetResult();

        await BillingTestHarness.WithTimeout(first);
        await BillingTestHarness.WithTimeout(second);

        Assert.AreEqual(1, SiteReads(harness));
        Assert.AreEqual(2, harness.Maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }
}
