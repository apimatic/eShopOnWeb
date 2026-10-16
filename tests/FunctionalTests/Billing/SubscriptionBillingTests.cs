using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.Billing;

public class SubscriptionBillingTests
{
    [Fact]
    public async Task UnknownProviderStateIsPreservedAndPeriodEndFallbackIsExplicit()
    {
        using var sandbox = new BillingSandbox();
        sandbox.TransformSubscription = s =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(s.GetRawText())!;
            node["state"] = "new_provider_state";
            node.AsObject().Remove("next_assessment_at");
            node["current_period_ends_at"] = "2026-12-16T12:30:00Z";
            return System.Text.Json.JsonSerializer.SerializeToElement(node);
        };
        using var db = sandbox.Context();
        var result = await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default);
        Assert.Equal("new_provider_state", result.Subscription!.State);
        Assert.Equal("current_period_ends_at", result.Subscription.NextBillingDateSource);
        Assert.Equal(DateTimeOffset.Parse("2026-12-16T12:30:00Z"), result.Subscription.NextBillingDate);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("product_price_in_cents")]
    public async Task UnusableSuccessRemainsPendingWithoutAdoptingWrongOwnershipOrInventingPrice(string field)
    {
        using var sandbox = new BillingSandbox();
        sandbox.TransformSubscription = s =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(s.GetRawText())!;
            node.AsObject().Remove(field);
            return System.Text.Json.JsonSerializer.SerializeToElement(node);
        };
        using (var db = sandbox.Context())
            Assert.Equal("pending", (await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default)).Status);
        using var sweepDb = sandbox.Context();
        await sandbox.Service(sweepDb).ReconcilePendingAsync(default);
        Assert.Equal(2, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.Contains(sweepDb.BillingClaims, c => c.State == "Unknown");
    }

    [Fact]
    public async Task CatalogFiltersFamilyAndArchivedAndUsesBaseUrlVerbatim()
    {
        using var sandbox = new BillingSandbox();
        using var db = sandbox.Context();
        var result = await sandbox.Service(db).ListPlansAsync(default);
        Assert.Equal(new[] { "pro", "basic" }, result.Plans.Select(p => p.Handle));
        Assert.False(result.IsPartial);
        Assert.All(result.Plans, p => Assert.Equal("remittance", p.PaymentCollectionMethod));
        var call = Assert.Single(sandbox.Requests);
        Assert.Equal("http://billing.invalid/proxy/products.json", call.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("200", QueryHelpers.ParseQuery(call.Uri.Query)["per_page"]);
    }

    [Fact]
    public async Task CatalogTraversesPagesAndRejectsForeignHandleBeforeAnyWrite()
    {
        using var sandbox = new BillingSandbox();
        sandbox.Products = uri => QueryHelpers.ParseQuery(uri.Query)["page"] == "1"
            ? BillingSandbox.Json(Enumerable.Range(1, 200).Select(i => BillingSandbox.Product(i, "p" + i, "foreign")))
            : BillingSandbox.Json(new[] { BillingSandbox.Product(201, "offered") });
        using var db = sandbox.Context();
        Assert.Equal("offered", Assert.Single((await sandbox.Service(db).ListPlansAsync(default)).Plans).Handle);
        var error = await Assert.ThrowsAsync<BillingException>(() => sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "p1", "A", "B", default));
        Assert.Equal(400, error.StatusCode);
        Assert.DoesNotContain(sandbox.Requests, r => r.Method == HttpMethod.Post);
        Assert.Empty(db.BillingClaims);
    }

    [Fact]
    public async Task NonProgressingFullPageIsExplicitlyPartialAndCannotEnroll()
    {
        using var sandbox = new BillingSandbox();
        sandbox.Products = _ => BillingSandbox.Json(Enumerable.Range(1, 200).Select(i => BillingSandbox.Product(i, "p" + i)));
        using var db = sandbox.Context();
        var result = await sandbox.Service(db).ListPlansAsync(default);
        Assert.True(result.IsPartial);
        Assert.Equal(3, result.NextPage);
        Assert.Equal(503, (await Assert.ThrowsAsync<BillingException>(() => sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "p1", "A", "B", default))).StatusCode);
        Assert.DoesNotContain(sandbox.Requests, r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("customer-drop")]
    [InlineData("subscription-drop")]
    [InlineData("customer-drift")]
    [InlineData("subscription-drift")]
    public async Task AmbiguousWriteRecoversByReferenceAndNeverResends(string fault)
    {
        using var sandbox = new BillingSandbox { Fault = fault };
        using var db = sandbox.Context();
        var result = await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "Ada", "Shopper", default);
        Assert.Equal("completed", result.Status);
        Assert.Equal(28700, result.Subscription!.PriceInCents); // Actual subscription price, not catalog price.
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(DateTimeOffset.Parse("2026-11-16T12:30:00Z"), result.Subscription.NextBillingDate);
        Assert.Equal("next_assessment_at", result.Subscription.NextBillingDateSource);
        Assert.Equal(2, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.All(db.BillingClaims, c => Assert.Equal("Completed", c.State));
        using var replayDb = sandbox.Context();
        var replay = await sandbox.Service(replayDb).SubscribeAsync(BillingSandbox.Shopper(), "pro", "Ada", "Shopper", default);
        Assert.False(replay.Created);
        Assert.Equal(result.Subscription.Id, replay.Subscription!.Id);
        Assert.Equal(2, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
        var sent = sandbox.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("subscriptions.json")).Body!;
        Assert.DoesNotContain("price", sent);
        using var payload = System.Text.Json.JsonDocument.Parse(sent);
        Assert.Equal("remittance", payload.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
        Assert.Equal("remittance", result.Subscription.PaymentCollectionMethod);
        Assert.DoesNotContain("payment_profile", sent);
        Assert.DoesNotContain("next_billing_at", sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownClaimRetainedAndSweepOnlyReadsUntilDelayedCommitAppears(bool customer)
    {
        using var sandbox = new BillingSandbox
        {
            Fault = customer ? "customer-drop" : "subscription-drop", PublishCustomer = !customer, PublishSubscription = customer
        };
        using (var db = sandbox.Context())
        {
            var result = await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default);
            Assert.Equal("pending", result.Status);
            Assert.Contains(db.BillingClaims, c => c.State == "Unknown");
        }
        var posts = sandbox.Requests.Count(r => r.Method == HttpMethod.Post);
        using (var replayDb = sandbox.Context())
        {
            Assert.Equal("pending", (await sandbox.Service(replayDb).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default)).Status);
            await sandbox.Service(replayDb).ReconcilePendingAsync(default);
        }
        Assert.Equal(posts, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
        sandbox.PublishDeferred();
        using var sweepDb = sandbox.Context();
        await sandbox.Service(sweepDb).ReconcilePendingAsync(default);
        Assert.All(sweepDb.BillingClaims, c => Assert.Equal("Completed", c.State));
        Assert.Equal(posts, sandbox.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ConcurrentRequestsAcrossContextsClaimBeforeProviderPost()
    {
        using var sandbox = new BillingSandbox();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            using var db = sandbox.Context();
            return await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default);
        })));
        Assert.Contains(results, r => r.Status == "completed");
        Assert.Equal(1, sandbox.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/customers.json")));
        Assert.Equal(1, sandbox.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/subscriptions.json")));
        using var finalDb = sandbox.Context();
        Assert.Equal(2, await finalDb.BillingClaims.CountAsync());
    }

    [Theory]
    [InlineData("customer-422")]
    [InlineData("subscription-422")]
    public async Task DefiniteRefusalIsSanitizedAndReleasesOnlyItsClaim(string fault)
    {
        using var sandbox = new BillingSandbox { Fault = fault };
        using var db = sandbox.Context();
        var ex = await Assert.ThrowsAsync<BillingException>(() => sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default));
        Assert.Equal(422, ex.StatusCode);
        Assert.DoesNotContain("sensitive", ex.Message);
        Assert.DoesNotContain(db.BillingClaims, c => c.State != "Completed");
    }

    [Theory]
    [InlineData(401, 502)]
    [InlineData(403, 502)]
    [InlineData(422, 422)]
    [InlineData(429, 503)]
    [InlineData(200, 502)]
    public async Task ReadErrorsAndDriftNeverMasqueradeAsAbsenceOrExposeSdkMessages(int providerStatus, int safeStatus)
    {
        using var sandbox = new BillingSandbox();
        sandbox.Reads = _ => BillingSandbox.Raw("{\"customer\":\"sensitive malformed value\"}", (HttpStatusCode)providerStatus);
        using var db = sandbox.Context();
        var ex = await Assert.ThrowsAsync<BillingException>(() => sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default));
        Assert.Equal(safeStatus, ex.StatusCode);
        Assert.DoesNotContain("billing.invalid", ex.Message);
        Assert.DoesNotContain("sensitive", ex.Message);
        Assert.DoesNotContain(sandbox.Requests, r => r.Method == HttpMethod.Post);
        Assert.Empty(db.BillingClaims);
    }

    [Fact]
    public async Task StableIdentityRecoversAfterRestartAndStoredReferenceSurvivesRename()
    {
        using var sandbox = new BillingSandbox();
        using (var db = sandbox.Context())
            await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(), "pro", "A", "B", default);
        var reference = sandbox.LastCustomerReference;
        using (var db = sandbox.Context())
        {
            await sandbox.Service(db).SubscribeAsync(BillingSandbox.Shopper(name: "RENAMED"), "basic", "A", "B", default);
            var mine = await sandbox.Service(db).ListMineAsync(BillingSandbox.Shopper(name: "RENAMED"), default);
            Assert.Equal(2, mine.Subscriptions.Count);
            Assert.All(db.BillingClaims, c => Assert.Equal(reference, c.CustomerReference));
            db.BillingClaims.RemoveRange(db.BillingClaims);
            await db.SaveChangesAsync();
        }
        using var restartDb = sandbox.Context();
        var recovered = await sandbox.Service(restartDb).SubscribeAsync(BillingSandbox.Shopper("reseeded-id"), "pro", "A", "B", default);
        Assert.Equal("completed", recovered.Status);
        Assert.False(recovered.Created);
        Assert.Equal(1, sandbox.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("customers.json")));
        Assert.Equal(2, sandbox.Requests.Count(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("subscriptions.json")));
    }
}
