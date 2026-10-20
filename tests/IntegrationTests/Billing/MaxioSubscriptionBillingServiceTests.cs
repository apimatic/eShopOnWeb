#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly Subscriber Shopper = new("demouser@microsoft.com", "demouser@microsoft.com");

    private readonly FakeMaxio _maxio = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase("maxio-" + Guid.NewGuid())
        .Options;
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        BaseUrl = "https://maxio.test",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    /// <summary>One service per "request": its own DbContext over the shared store, as in the app.</summary>
    private MaxioSubscriptionBillingService NewService()
    {
        var client = MaxioClientFactory.Create(new HttpClient(_maxio), _settings, NullLoggerFactory.Instance);
        var gateway = new MaxioBillingGateway(client, NullLogger<MaxioBillingGateway>.Instance);
        return new MaxioSubscriptionBillingService(gateway, new CatalogContext(_dbOptions), _cache,
            Options.Create(_settings), _clock, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private CatalogContext Db() => new(_dbOptions);

    [Fact]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        var catalog = await NewService().ListPlansAsync();

        Assert.False(catalog.Truncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        Assert.Equal(29900, catalog.Plans[0].PriceInCents);
        Assert.Equal("month", catalog.Plans[0].IntervalUnit);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionAndReturnsPlanPriceStateAndNextBilling()
    {
        var result = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal("eshop-pro", result.Subscription!.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(1, _maxio.CreateCustomerPosts);
        Assert.Equal(1, _maxio.CreateSubscriptionPosts);
        Assert.True(_maxio.CustomersByReference.ContainsKey(Shopper.UserName));
        Assert.Equal("remittance", _maxio.Subscriptions.Single()["payment_collection_method"]!.GetValue<string>());

        await using var db = Db();
        var claim = await db.MaxioSubscriptionClaims.SingleAsync();
        Assert.Equal(MaxioClaimState.Created, claim.State);
        Assert.Equal(result.Subscription.SubscriptionId, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task ConcurrentDoubleSubmitSendsExactlyOneCreateToMaxio()
    {
        _maxio.CreateSubscriptionDelay = TimeSpan.FromMilliseconds(500);

        var first = NewService().SubscribeAsync(Shopper, "eshop-pro");
        var second = NewService().SubscribeAsync(Shopper, "eshop-pro");
        var outcomes = await Task.WhenAll(Capture(first), Capture(second));

        Assert.Equal(1, _maxio.CreateSubscriptionPosts);
        Assert.Single(_maxio.Subscriptions);
        Assert.Single(outcomes, o => o.Result?.Outcome == SubscribeOutcome.Created);
        Assert.Single(outcomes, o => o.Error is DuplicateException);
    }

    [Fact]
    public async Task RepeatSubscribeToSamePlanReturnsExistingSubscriptionWithoutSecondCreate()
    {
        var created = await NewService().SubscribeAsync(Shopper, "eshop-pro");
        var repeat = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, repeat.Outcome);
        Assert.Equal(created.Subscription!.SubscriptionId, repeat.Subscription!.SubscriptionId);
        Assert.Equal(1, _maxio.CreateSubscriptionPosts);
    }

    [Fact]
    public async Task SubscribingToAnotherPlanWhileSubscribedIsRefused()
    {
        await NewService().SubscribeAsync(Shopper, "eshop-pro");

        await Assert.ThrowsAsync<DuplicateException>(() => NewService().SubscribeAsync(Shopper, "basic-plan"));
        Assert.Equal(1, _maxio.CreateSubscriptionPosts);
    }

    [Fact]
    public async Task PlanNotOfferedByTheFamilyIsRefusedBeforeAnyWrite()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => NewService().SubscribeAsync(Shopper, "api-call"));

        Assert.Equal(0, _maxio.CreateCustomerPosts);
        Assert.Equal(0, _maxio.CreateSubscriptionPosts);
        await using var db = Db();
        Assert.Empty(db.MaxioSubscriptionClaims);
    }

    [Fact]
    public async Task ExistingProviderStateIsAdoptedWhenLocalClaimsAreGone()
    {
        // Simulates a restart of the in-memory store: Maxio still holds the customer and subscription.
        await NewService().SubscribeAsync(Shopper, "eshop-pro");
        await using (var db = Db())
        {
            db.MaxioSubscriptionClaims.RemoveRange(db.MaxioSubscriptionClaims);
            db.MaxioCustomerClaims.RemoveRange(db.MaxioCustomerClaims);
            await db.SaveChangesAsync();
        }

        var result = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(1, _maxio.CreateCustomerPosts);
        Assert.Equal(1, _maxio.CreateSubscriptionPosts);
    }

    [Fact]
    public async Task ConnectionDroppedAfterMaxioCreatedSubscriptionIsSettledAsCreated()
    {
        _maxio.DropConnectionAfterCreatingSubscription = true;

        var result = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(_maxio.Subscriptions.Single()["id"]!.GetValue<int>(), result.Subscription!.SubscriptionId);
        await using var db = Db();
        Assert.Equal(MaxioClaimState.Created, (await db.MaxioSubscriptionClaims.SingleAsync()).State);
    }

    [Fact]
    public async Task ConnectionDroppedBeforeMaxioActedIsPendingThenSettledWithoutDuplicate()
    {
        _maxio.DropConnectionBeforeCreatingSubscription = true;

        var first = await NewService().SubscribeAsync(Shopper, "eshop-pro");
        Assert.Equal(SubscribeOutcome.Pending, first.Outcome);
        Assert.Empty(_maxio.Subscriptions);

        // While unsettled the claim blocks a second create.
        var stillPending = await NewService().SubscribeAsync(Shopper, "eshop-pro");
        Assert.Equal(SubscribeOutcome.Pending, stillPending.Outcome);
        Assert.Equal(1, _maxio.CreateSubscriptionPosts);

        // Once the claim is stale and Maxio still has nothing for its reference, it is released and retried once.
        _clock.Advance(_settings.PendingClaimStaleAfter + TimeSpan.FromSeconds(1));
        var settled = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, settled.Outcome);
        Assert.Equal(2, _maxio.CreateSubscriptionPosts);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task ProviderRefusalReleasesTheClaimSoTheUserCanRetry()
    {
        _maxio.RejectNextSubscriptionWith = new[] { "No payment method was on file" };

        var refusal = await Assert.ThrowsAsync<BillingProviderException>(() => NewService().SubscribeAsync(Shopper, "eshop-pro"));
        Assert.Equal(BillingFailureKind.Rejected, refusal.Kind);
        Assert.Equal(422, refusal.ProviderStatusCode);
        Assert.Contains("No payment method was on file", refusal.Message);
        await using (var db = Db())
        {
            Assert.Empty(db.MaxioSubscriptionClaims);
        }

        var retry = await NewService().SubscribeAsync(Shopper, "eshop-pro");
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
        Assert.Equal(1, _maxio.CreateCustomerPosts);
    }

    [Fact]
    public async Task ConnectionDroppedAfterMaxioCreatedCustomerAdoptsThatCustomer()
    {
        _maxio.DropConnectionAfterCreatingCustomer = true;

        var result = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, _maxio.CreateCustomerPosts);
        Assert.Single(_maxio.CustomersByReference);
        await using var db = Db();
        var claim = await db.MaxioCustomerClaims.SingleAsync();
        Assert.Equal(MaxioClaimState.Created, claim.State);
        Assert.Equal(_maxio.CustomersByReference[Shopper.UserName], claim.MaxioCustomerId);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForAUserWithNoBillingAccount()
    {
        var subscriptions = await NewService().ListSubscriptionsAsync("nobody@microsoft.com");

        Assert.Empty(subscriptions);
        Assert.Equal(0, _maxio.CreateCustomerPosts);
    }

    [Fact]
    public async Task MySubscriptionsReflectsTheNewSubscription()
    {
        var created = await NewService().SubscribeAsync(Shopper, "eshop-pro");

        var mine = await NewService().ListSubscriptionsAsync(Shopper.UserName);

        var subscription = Assert.Single(mine);
        Assert.Equal(created.Subscription!.SubscriptionId, subscription.SubscriptionId);
        Assert.Equal("Pro Plan", subscription.PlanName);
    }

    [Fact]
    public void SettingsValidationNamesEveryMissingKeyWithoutEchoingValues()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = "  " });

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
        Assert.Contains("Maxio:Subdomain", result.FailureMessage);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    private static async Task<(SubscribeResult? Result, Exception? Error)> Capture(Task<SubscribeResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
