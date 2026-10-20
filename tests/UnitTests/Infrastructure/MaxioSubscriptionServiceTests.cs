using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness
    {
        public FakeMaxioHandler Maxio { get; } = new();
        public MutableClock Clock { get; } = new();
        public DbContextOptions<CatalogContext> DbOptions { get; } =
            new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase("maxio-" + Guid.NewGuid()).Options;
        public SubscriberIdentity User { get; } = new("user-1", "shopper@example.test", "shopper@example.test");

        public MaxioSubscriptionService NewService()
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
                Retry = RetryOptions.Default() with { MaxRetries = 0, Timeout = TimeSpan.FromSeconds(5) },
            };
            options.Server.Production.Us.BaseUrl = "http://maxio.test";
            var client = new MaxioAdvancedBillingClient(new HttpClient(Maxio), options);
            var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" });
            return new MaxioSubscriptionService(client, new CatalogContext(DbOptions), settings, Clock, NullLogger<MaxioSubscriptionService>.Instance);
        }

        public MaxioSubscriptionLink? Row(string userId)
        {
            using var db = new CatalogContext(DbOptions);
            return db.MaxioSubscriptionLinks.AsNoTracking().FirstOrDefault(l => l.UserId == userId);
        }
    }

    [Fact]
    public async Task ListPlans_MapsActivePlansAndFlagsDefault()
    {
        var h = new Harness();
        var listing = await h.NewService().ListPlansAsync(default);

        Assert.False(listing.IsPartial);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, listing.Plans.Select(p => p.Handle));
        var pro = listing.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.True(pro.IsDefault);
        Assert.False(listing.Plans.Single(p => p.Handle == "basic-plan").IsDefault);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_AndReturnsPlanPriceStateNextBilling()
    {
        var h = new Harness();
        var result = await h.NewService().SubscribeAsync(h.User, null, default);

        Assert.True(result.Created);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);
        Assert.Equal(1, h.Maxio.Count("POST", "/customers.json"));
        Assert.Equal(1, h.Maxio.Count("POST", "/subscriptions.json"));
        // Customer reference is the user id; the customer id sent on the subscription is the one Maxio returned.
        Assert.Contains("\"reference\":\"user-1\"", h.Maxio.Bodies[0]);
        Assert.Contains("\"customer_id\":100", h.Maxio.Bodies[1]);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", h.Maxio.Bodies[1]);
        Assert.Equal(MaxioSubscriptionLinkState.Active, h.Row("user-1")!.State);
    }

    [Fact]
    public async Task Subscribe_Twice_ReturnsExistingAndNeverCreatesASecondCustomerOrSubscription()
    {
        var h = new Harness();
        var first = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);
        var second = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(1, h.Maxio.CustomerCount);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task Subscribe_DifferentPlanWhileSubscribed_Conflicts()
    {
        var h = new Harness();
        await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "basic-plan", default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task Subscribe_Concurrent_DoubleClick_ReachesMaxioOnce()
    {
        var h = new Harness();
        h.Maxio.CreateSubscriptionDelay = TimeSpan.FromMilliseconds(300);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { return (Result: await h.NewService().SubscribeAsync(h.User, "eshop-pro", default), Error: (SubscriptionException?)null); }
            catch (SubscriptionException e) { return (Result: null, Error: e); }
        }));

        Assert.Equal(1, h.Maxio.CustomerCount);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
        Assert.Equal(1, h.Maxio.Count("POST", "/subscriptions.json"));
        Assert.Single(results, r => r.Result is { Created: true });
        // Losers are either told it is in flight (409) or handed the finished subscription.
        Assert.All(results.Where(r => r.Error is not null), r => Assert.Equal(409, r.Error!.StatusCode));
    }

    [Fact]
    public async Task Claim_SecondInsertOfSameUserIsRefusedByTheStore()
    {
        // The EF in-memory provider raises a raw ArgumentException (not DbUpdateException) for a duplicate key across contexts; SQL Server raises DbUpdateException. ClaimAsync catches all three.
        var h = new Harness();
        MaxioSubscriptionLink NewRow() => new() { UserId = "dup", ProductHandle = "eshop-pro" };
        await using (var a = new CatalogContext(h.DbOptions)) { a.MaxioSubscriptionLinks.Add(NewRow()); await a.SaveChangesAsync(); }
        await using var b = new CatalogContext(h.DbOptions);
        b.MaxioSubscriptionLinks.Add(NewRow());
        var ex = await Record.ExceptionAsync(() => b.SaveChangesAsync());
        Assert.True(ex is DbUpdateException or InvalidOperationException or ArgumentException, ex?.GetType().FullName);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Is400_AndWritesNothing()
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "not-offered", default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, h.Maxio.Count("POST", ""));
        Assert.Null(h.Row("user-1"));
    }

    [Fact]
    public async Task Subscribe_ArchivedPlan_IsNotOffered()
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "old-plan", default));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_422FromMaxio_ReleasesTheClaimSoALaterAttemptWorks()
    {
        var h = new Harness();
        h.Maxio.Reject422CreateSubscription = true;
        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "eshop-pro", default));
        Assert.Equal(422, ex.StatusCode);
        Assert.DoesNotContain("Product is invalid", ex.Message); // provider detail is not echoed
        Assert.Null(h.Row("user-1"));

        h.Maxio.Reject422CreateSubscription = false;
        var ok = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);
        Assert.True(ok.Created);
        Assert.Equal(1, h.Maxio.CustomerCount); // customer was reused, not recreated
    }

    [Fact]
    public async Task CreateCustomer_ConnectionDropsAfterLanding_IsSettledByReadingBackByReference()
    {
        var h = new Harness();
        h.Maxio.LandThenDropCreateCustomer = true;

        var result = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal(1, h.Maxio.CustomerCount);
        Assert.Equal(1, h.Maxio.Count("POST", "/customers.json"));
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task CreateSubscription_ConnectionDropsAfterLanding_IsSettledByListingCustomerSubscriptions()
    {
        var h = new Harness();
        h.Maxio.LandThenDropCreateSubscription = true;

        var result = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
        Assert.Equal(1, h.Maxio.Count("POST", "/subscriptions.json")); // POST never resent
        Assert.Equal(MaxioSubscriptionLinkState.Active, h.Row("user-1")!.State);
    }

    [Fact]
    public async Task CreateSubscription_ConnectionDropsAndReadBackFails_KeepsUnknownRow_ThenNextCallSettles()
    {
        var h = new Harness();
        h.Maxio.LandThenDropCreateSubscription = true;
        h.Maxio.FailListSubscriptions = true;

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "eshop-pro", default));
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(MaxioSubscriptionLinkState.Unknown, h.Row("user-1")!.State);

        // Maxio recovers: my-subscriptions settles the unknown row by adopting the subscription that landed.
        h.Maxio.FailListSubscriptions = false;
        h.Maxio.LandThenDropCreateSubscription = false;
        var mine = await h.NewService().GetSubscriptionsAsync(h.User, default);
        Assert.Single(mine);
        Assert.Equal(MaxioSubscriptionLinkState.Active, h.Row("user-1")!.State);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task CreateSubscription_DropBeforeLanding_UnknownRowIsReleasedByNextSubscribe_WhichCreatesExactlyOne()
    {
        var h = new Harness();
        h.Maxio.DropBeforeCreateSubscription = true;
        h.Maxio.FailListSubscriptions = true;
        await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "eshop-pro", default));
        Assert.Equal(MaxioSubscriptionLinkState.Unknown, h.Row("user-1")!.State);

        h.Maxio.DropBeforeCreateSubscription = false;
        h.Maxio.FailListSubscriptions = false;
        var result = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);

        Assert.True(result.Created);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task Subscribe_StaleClaimingRow_IsSettledAndSubscribeProceeds_FreshClaimingRowConflicts()
    {
        var h = new Harness();
        await using (var db = new CatalogContext(h.DbOptions))
        {
            db.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink
            {
                UserId = "user-1", ProductHandle = "eshop-pro", State = MaxioSubscriptionLinkState.Claiming,
                ClaimedAtUtc = h.Clock.Now, UpdatedAtUtc = h.Clock.Now
            });
            await db.SaveChangesAsync();
        }

        var conflict = await Assert.ThrowsAsync<SubscriptionException>(() => h.NewService().SubscribeAsync(h.User, "eshop-pro", default));
        Assert.Equal(409, conflict.StatusCode);

        h.Clock.Now += MaxioSubscriptionService.StaleClaimAfter + TimeSpan.FromSeconds(1);
        var result = await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);
        Assert.True(result.Created);
        Assert.Equal(1, h.Maxio.SubscriptionCount);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsLiveStateFromMaxio_AndPassesUnknownStatesThrough()
    {
        var h = new Harness();
        Assert.Empty(await h.NewService().GetSubscriptionsAsync(h.User, default));

        await h.NewService().SubscribeAsync(h.User, "basic-plan", default);
        h.Maxio.SubscriptionState = "some_future_state";

        var mine = await h.NewService().GetSubscriptionsAsync(h.User, default);
        var sub = Assert.Single(mine);
        Assert.Equal("basic-plan", sub.PlanHandle);
        Assert.Equal(2900, sub.PriceInCents);
        Assert.Equal("some_future_state", sub.State);
        Assert.NotNull(sub.NextBillingDate);
    }

    [Fact]
    public async Task GetSubscriptions_OnlyReturnsTheCallersOwnSubscription()
    {
        var h = new Harness();
        await h.NewService().SubscribeAsync(h.User, "eshop-pro", default);
        var other = new SubscriberIdentity("user-2", "other@example.test", "other@example.test");

        Assert.Empty(await h.NewService().GetSubscriptionsAsync(other, default));
    }

    [Fact]
    public void Settings_ValidationNamesMissingKeys_NeverValues()
    {
        var problems = new MaxioSettings { ApiKey = " ", Subdomain = "s", ProductFamilyHandle = null, BaseUrl = "not a url" }.Validate();
        Assert.Contains(problems, p => p.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, p => p.Contains("Maxio:ProductFamilyHandle"));
        Assert.Contains(problems, p => p.Contains("Maxio:BaseUrl"));
        Assert.DoesNotContain(problems, p => p.Contains("Maxio:Subdomain"));
    }
}
