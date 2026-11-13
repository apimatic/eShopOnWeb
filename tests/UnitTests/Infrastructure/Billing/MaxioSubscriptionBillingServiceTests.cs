using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly string CustomerReference = MaxioReferences.ForCustomer("demouser@microsoft.com");

    [Fact]
    public async Task GetPlans_ReturnsUnarchivedFamilyProducts()
    {
        using var host = new BillingTestHost();

        var catalog = await host.InRequest(s => s.GetPlansAsync());

        Assert.True(catalog.IsComplete);
        Assert.Equal(new[] { "pro", "basic" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        var request = Assert.Single(host.Maxio.Requests);
        Assert.Equal("unit-test-site.chargify.com", request.Uri.Host);
        Assert.Equal("/product_families/handle:test-family/products.json", Uri.UnescapeDataString(request.Uri.AbsolutePath));
        Assert.Contains("per_page=200", request.Uri.Query);
    }

    [Fact]
    public async Task GetPlans_FlagsTheListingIncomplete_WhenThePageCapIsReached()
    {
        using var host = new BillingTestHost();
        host.Maxio.Products.Clear();
        for (var i = 0; i < MaxioSubscriptionBillingService_PageCap + 1; i++)
        {
            host.Maxio.WithProduct("plan-" + i, "Plan " + i, 100);
        }

        var catalog = await host.InRequest(s => s.GetPlansAsync());

        Assert.False(catalog.IsComplete);
        Assert.Equal(MaxioSubscriptionBillingService_PageCap, catalog.Plans.Count);
        Assert.Equal(5, host.Maxio.Requests.Count);
    }

    private const int MaxioSubscriptionBillingService_PageCap = 200 * 5;

    [Fact]
    public async Task GetPlans_UnknownProductFamily_IsAProviderFailure()
    {
        using var host = new BillingTestHost(new Dictionary<string, string?> { ["Maxio:ProductFamilyHandle"] = "no-such-family" });

        var ex = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.GetPlansAsync()));

        Assert.Equal(BillingErrorKind.ProviderFailure, ex.Kind);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_AndReportsPlanPriceStateAndNextBilling()
    {
        using var host = new BillingTestHost();

        var result = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        var sub = Assert.IsType<SubscriptionDetails>(result.Subscription);
        Assert.Equal("pro", sub.PlanHandle);
        Assert.Equal(29900, sub.PriceInCents);
        Assert.Equal("active", sub.State);
        Assert.NotNull(sub.NextBillingAt);
        Assert.Equal(MaxioReferences.ForSubscription(CustomerReference, "pro"), result.Reference);

        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        var customerBody = JsonNode.Parse(host.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/customers.json").Body!)!["customer"]!;
        Assert.Equal(CustomerReference, customerBody["reference"]!.GetValue<string>());
        Assert.Equal("demouser@microsoft.com", customerBody["email"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(customerBody["first_name"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(customerBody["last_name"]!.GetValue<string>()));

        var subscriptionBody = JsonNode.Parse(host.Maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/subscriptions.json").Body!)!["subscription"]!;
        Assert.Equal("pro", subscriptionBody["product_handle"]!.GetValue<string>());
        Assert.Equal(result.Reference, subscriptionBody["reference"]!.GetValue<string>());
        Assert.Equal(host.Maxio.CustomersByReference[CustomerReference]["id"]!.GetValue<int>(), subscriptionBody["customer_id"]!.GetValue<int>());
        Assert.Null(subscriptionBody["customer_attributes"]);
    }

    [Theory]
    [InlineData(true, "remittance")]
    [InlineData(false, "invoice")]
    public async Task Subscribe_UsesTheInvoiceStyleCollectionMethodOfTheSitesArchitecture(bool relationshipInvoicing, string expected)
    {
        using var host = new BillingTestHost();
        host.Maxio.RelationshipInvoicingEnabled = relationshipInvoicing;

        await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "basic"));

        var sent = host.Maxio.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/subscriptions.json")
            .Select(r => JsonNode.Parse(r.Body!)!["subscription"]!["payment_collection_method"]!.GetValue<string>())
            .ToList();
        Assert.Equal(new[] { expected, expected }, sent);
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Get, "/site.json")); // read once, then cached
    }

    [Fact]
    public async Task Subscribe_WhenTheSiteArchitectureIsUnknown_IsUnavailableAndWritesNothing()
    {
        using var host = new BillingTestHost();
        host.Maxio.RelationshipInvoicingEnabled = null;

        var ex = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro")));

        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);
        Assert.DoesNotContain(host.Maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_Twice_ReturnsTheExistingSubscription_WithoutASecondWrite()
    {
        using var host = new BillingTestHost();

        var first = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        var second = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));

        Assert.Equal(SubscribeOutcome.Created, first.Outcome);
        Assert.Equal(SubscribeOutcome.AlreadyExists, second.Outcome);
        Assert.Equal(first.Subscription!.Id, second.Subscription!.Id);
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_DoubleClick_ConcurrentRequestsReachMaxioOnce()
    {
        using var host = new BillingTestHost();
        host.Maxio.SubscriptionCreateDelay = TimeSpan.FromMilliseconds(300);

        var attempts = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => Capture(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro")))))
            .ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(host.Maxio.Subscriptions);
        Assert.Contains(outcomes, o => o.Result?.Outcome == SubscribeOutcome.Created);
        Assert.All(outcomes, o => Assert.True(
            o.Result is not null || o.Error?.Kind == BillingErrorKind.InProgress,
            $"unexpected failure: {o.Error?.Kind} {o.Error?.Message}"));
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("legacy")] // archived: not returned by the listing
    [InlineData("PRO")]    // handles match exactly
    public async Task Subscribe_ToAPlanTheFamilyDoesNotOffer_IsRejectedBeforeAnyWrite(string handle)
    {
        using var host = new BillingTestHost();

        var ex = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), handle)));

        Assert.Equal(BillingErrorKind.InvalidRequest, ex.Kind);
        Assert.DoesNotContain(host.Maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_ConnectionDropsAfterMaxioCreatedIt_IsSettledByReference()
    {
        using var host = new BillingTestHost();
        host.Maxio.DropConnectionAfterNextSubscriptionCreate = true;

        var result = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.NotNull(result.Subscription);
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(host.Maxio.Subscriptions);
        var claim = await FindEnrollment(host, result.Reference);
        Assert.Equal(BillingClaimStatus.Completed, claim!.Status);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsAndOutcomeCannotBeConfirmed_StaysPendingThenRetryCreatesOnce()
    {
        using var host = new BillingTestHost();
        var failSubscriptionPost = true;
        host.Maxio.Intercept = (request, _) =>
        {
            if (failSubscriptionPost && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                failSubscriptionPost = false;
                throw new HttpRequestException("connection refused"); // never reached Maxio
            }

            return null;
        };

        var first = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        Assert.Equal(SubscribeOutcome.Pending, first.Outcome);
        Assert.Equal(BillingClaimStatus.Pending, (await FindEnrollment(host, first.Reference))!.Status);

        // Within the cool-down a retry is refused rather than risking a duplicate.
        var early = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro")));
        Assert.Equal(BillingErrorKind.InProgress, early.Kind);

        host.Clock.Advance(MaxioSubscriptionBillingService_UnknownCoolDown);
        var retry = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));

        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
        Assert.Equal(first.Reference, retry.Reference);
        Assert.Single(host.Maxio.Subscriptions);
    }

    private static readonly TimeSpan MaxioSubscriptionBillingService_UnknownCoolDown = TimeSpan.FromSeconds(31);

    [Fact]
    public async Task Subscribe_PendingOutcomeThatLanded_IsSettledByMySubscriptions()
    {
        using var host = new BillingTestHost();
        host.Maxio.DropConnectionAfterNextSubscriptionCreate = true;
        var lookupFails = false;
        host.Maxio.Intercept = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                lookupFails = true; // every read after the ambiguous create fails, so it cannot be settled now
                return null;
            }

            var settlingRead = path == "/subscriptions/lookup.json" || (path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"));
            return lookupFails && request.Method == HttpMethod.Get && settlingRead
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) // not retried by the SDK
                : null;
        };

        var first = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        Assert.Equal(SubscribeOutcome.Pending, first.Outcome);

        lookupFails = false;
        var mine = await host.InRequest(s => s.GetSubscriptionsAsync("demouser@microsoft.com"));

        Assert.Single(mine);
        Assert.Equal(BillingClaimStatus.Completed, (await FindEnrollment(host, first.Reference))!.Status);
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_RejectedByMaxio_ReturnsItsMessages_AndFreesTheClaim()
    {
        using var host = new BillingTestHost();
        var reject = true;
        host.Maxio.Intercept = (request, _) =>
        {
            if (reject && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json")
            {
                reject = false;
                return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent("""{"errors":["Billing address is required."]}""", Encoding.UTF8, "application/json")
                };
            }

            return null;
        };

        var ex = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro")));
        Assert.Equal(BillingErrorKind.Rejected, ex.Kind);
        Assert.Contains("Billing address is required.", ex.Details);

        var retry = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
    }

    [Fact]
    public async Task Subscribe_CustomerCreateConnectionDrop_AdoptsTheCustomerMaxioCreated()
    {
        using var host = new BillingTestHost();
        host.Maxio.DropConnectionAfterNextCustomerCreate = true;

        var result = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "basic"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Single(host.Maxio.CustomersByReference);
    }

    [Fact]
    public async Task Subscribe_ReusesAMaxioCustomerThatAlreadyExists_AfterARestart()
    {
        using var host = new BillingTestHost();
        host.Maxio.CustomersByReference[CustomerReference] = new JsonObject
        {
            ["id"] = 77, ["reference"] = CustomerReference, ["email"] = "demouser@microsoft.com"
        };

        var result = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(0, host.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(77, JsonNode.Parse(host.Maxio.Requests.Single(r => r.Method == HttpMethod.Post).Body!)!["subscription"]!["customer_id"]!.GetValue<int>());
    }

    [Fact]
    public async Task MySubscriptions_WithoutABillingCustomer_IsEmptyAndWritesNothing()
    {
        using var host = new BillingTestHost();

        var mine = await host.InRequest(s => s.GetSubscriptionsAsync("nobody@microsoft.com"));

        Assert.Empty(mine);
        Assert.DoesNotContain(host.Maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task MySubscriptions_ListsOnlyTheCallersSubscriptions()
    {
        using var host = new BillingTestHost();
        await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile("someone.else@microsoft.com"), "basic"));

        var mine = await host.InRequest(s => s.GetSubscriptionsAsync("DemoUser@Microsoft.com"));

        var only = Assert.Single(mine);
        Assert.Equal("pro", only.PlanHandle);
    }

    [Fact]
    public async Task Subscribe_ProviderOutage_IsUnavailable_AndFreesTheClaim()
    {
        using var host = new BillingTestHost();
        var outage = true;
        host.Maxio.Intercept = (request, _) =>
            outage && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/customers.json"
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : null;

        var ex = await Assert.ThrowsAsync<BillingException>(() => host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro")));
        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);

        outage = false;
        var retry = await host.InRequest(s => s.SubscribeAsync(BillingTestHost.Profile(), "pro"));
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
    }

    [Theory]
    [InlineData("https://billing-proxy.example.test/maxio")]
    [InlineData("https://billing-proxy.example.test/maxio/")]
    public async Task BaseUrlOverride_IsUsedVerbatimInsteadOfTheSubdomain(string baseUrl)
    {
        using var host = new BillingTestHost(new Dictionary<string, string?> { ["Maxio:BaseUrl"] = baseUrl, ["Maxio:Subdomain"] = null });
        host.Maxio.BasePath = "/maxio";

        var catalog = await host.InRequest(s => s.GetPlansAsync());

        Assert.Equal(2, catalog.Plans.Count);

        var uri = Assert.Single(host.Maxio.Requests).Uri;
        Assert.Equal("billing-proxy.example.test", uri.Host);
        Assert.Equal("/maxio/product_families/handle:test-family/products.json", Uri.UnescapeDataString(uri.AbsolutePath));
    }

    private sealed record Attempt(SubscribeResult? Result, BillingException? Error);

    private static async Task<Attempt> Capture(Func<Task<SubscribeResult>> action)
    {
        try
        {
            return new Attempt(await action(), null);
        }
        catch (BillingException ex)
        {
            return new Attempt(null, ex);
        }
    }

    private static Task<SubscriptionEnrollment?> FindEnrollment(BillingTestHost host, string reference)
        => host.InScope(sp => sp.GetRequiredService<CatalogContext>().SubscriptionEnrollments.AsNoTracking().SingleOrDefaultAsync(e => e.Reference == reference));
}
