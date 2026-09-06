using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProFamily = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";
    private const string BasicPlan = "basic-plan";

    private readonly FakeMaxioApiClient _client = new();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = ProFamily,
        // Keep the tests fast: no caching between calls, no sleeping between duplicate re-reads.
        CatalogCacheSeconds = 0,
        DuplicateResolutionDelayMilliseconds = 0
    };

    private readonly SubscriberIdentity _subscriber =
        SubscriberIdentity.FromAccount("user-1", "Demouser@Microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        _client.Products.Add(NewProduct(ProPlan, "Pro Plan", 29900));
        _client.Products.Add(NewProduct(BasicPlan, "Basic Plan", 2900));
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsActivePlansCheapestFirst_WithSiteCurrency()
    {
        var archived = NewProduct("retired-plan", "Retired Plan", 100);
        archived.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _client.Products.Add(archived);

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { BasicPlan, ProPlan }, plans.Select(p => p.Handle).ToArray());
        Assert.Equal(299m, plans.Single(p => p.Handle == ProPlan).Price);
        Assert.All(plans, p => Assert.Equal("USD", p.Currency));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_ForANewShopper()
    {
        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(SubscriptionStates.Active, result.Subscription.State);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(1, _client.CustomerCreateCount);
        Assert.Equal(1, _client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task SubscribeAsync_UsesTheEmailDerivedReference_SoTheCustomerIsFoundAgain()
    {
        // Same account, different casing and padding on the email.
        var reference = MaxioCustomerReference.For(_subscriber);
        _client.SeedCustomer(reference);

        await CreateService().SubscribeAsync(
            SubscriberIdentity.FromAccount("user-1", " demouser@microsoft.com "), ProPlan);

        Assert.Equal(0, _client.CustomerCreateCount);
        Assert.Single(_client.Customers);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscription_WhenAlreadyOnThePlan()
    {
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        var seeded = _client.SeedSubscription(customer.Id, ProPlan, SubscriptionStates.Active);

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.AlreadyEnrolled);
        Assert.Equal(seeded.Id.ToString(), result.Subscription.Id);
        Assert.Equal(0, _client.SubscriptionCreateCount);
    }

    [Theory]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.Unpaid)]
    [InlineData(SubscriptionStates.OnHold)]
    public async Task SubscribeAsync_DoesNotDuplicate_WhenTheExistingSubscriptionIsInAProblemState(string state)
    {
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.SeedSubscription(customer.Id, ProPlan, state);

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.AlreadyEnrolled);
        Assert.Equal(0, _client.SubscriptionCreateCount);
    }

    [Theory]
    [InlineData(SubscriptionStates.Canceled)]
    [InlineData(SubscriptionStates.Expired)]
    [InlineData(SubscriptionStates.TrialEnded)]
    public async Task SubscribeAsync_SubscribesAgain_WhenTheOnlySubscriptionHasEnded(string state)
    {
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.SeedSubscription(customer.Id, ProPlan, state);

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(1, _client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresASubscriptionToADifferentPlan()
    {
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.SeedSubscription(customer.Id, BasicPlan, SubscriptionStates.Active);

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.AlreadyEnrolled);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesOneCustomerAndOneSubscription_WhenSubscribeIsDoubleClicked()
    {
        // A wide write window makes both requests overlap, which is exactly the double-click case.
        // Two service instances, as two concurrent HTTP requests would get, sharing the process-wide lock.
        _client.WriteDelay = TimeSpan.FromMilliseconds(150);

        var results = await Task.WhenAll(
            CreateService().SubscribeAsync(_subscriber, ProPlan),
            CreateService().SubscribeAsync(_subscriber, ProPlan));

        Assert.Equal(1, _client.CustomerCreateCount);
        Assert.Equal(1, _client.SubscriptionCreateCount);
        Assert.Single(_client.Subscriptions);
        Assert.Equal(results[0].Subscription.Id, results[1].Subscription.Id);
        // Exactly one of the two callers is told it created the subscription.
        Assert.Single(results.Where(r => !r.AlreadyEnrolled));
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesTheWinnersSubscription_WhenMaxioRejectsADuplicateWrite()
    {
        // Stands in for a second app instance having already submitted the identical subscribe.
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.AlwaysRejectSubscriptionAsDuplicate = true;
        _client.HideSubscriptionForReads = 1;
        _client.SubscriptionAppearingAfterReads = new MaxioSubscription
        {
            Id = 777,
            State = SubscriptionStates.Active,
            Customer = customer,
            Product = _client.Products.Single(p => p.Handle == ProPlan),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.AlreadyEnrolled);
        Assert.Equal("777", result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_Throws409_WhenADuplicateWriteCannotBeResolvedToASubscription()
    {
        _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.AlwaysRejectSubscriptionAsDuplicate = true;

        await Assert.ThrowsAsync<SubscriptionConflictException>(
            () => CreateService().SubscribeAsync(_subscriber, ProPlan));
    }

    [Fact]
    public async Task SubscribeAsync_ReusesTheCustomer_WhenMaxioSaysTheReferenceIsAlreadyTaken()
    {
        // The customer exists, but our lookup misses it - as it would if another instance created it
        // moments earlier. Maxio's reference uniqueness constraint then rejects our create.
        var existing = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.HideCustomerForReads = 1;

        var result = await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.Equal(0, _client.CustomerCreateCount);
        Assert.Single(_client.Customers);
        Assert.Equal(existing.Id.ToString(), result.Subscription.CustomerId);
    }

    [Fact]
    public async Task SubscribeAsync_UsesTheConfiguredDefaultPlan_WhenTheRequestNamesNone()
    {
        _settings.DefaultPlanHandle = BasicPlan;

        var result = await CreateService().SubscribeAsync(_subscriber, planHandle: null);

        Assert.Equal(BasicPlan, result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWithTheAvailableHandles_ForAnUnknownPlan()
    {
        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, "not-a-plan"));

        Assert.Equal(new[] { BasicPlan, ProPlan }, exception.AvailableHandles.ToArray());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenNoPlanIsNamedAndNoDefaultIsConfigured()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, planHandle: null));
    }

    [Fact]
    public async Task SubscribeAsync_InvoicesInsteadOfCharging_SoNoPaymentMethodIsNeeded()
    {
        await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.Equal("remittance", _client.LastSubscriptionAttributes!.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_UsesLegacyInvoiceCollection_OnAStatementsArchitectureSite()
    {
        _client.Site = new MaxioSite { Currency = "USD", RelationshipInvoicingEnabled = false };

        await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.Equal("invoice", _client.LastSubscriptionAttributes!.PaymentCollectionMethod);
    }

    [Fact]
    public async Task SubscribeAsync_HonoursAnExplicitPaymentCollectionMethod()
    {
        _settings.PaymentCollectionMethod = "automatic";

        await CreateService().SubscribeAsync(_subscriber, ProPlan);

        Assert.Equal("automatic", _client.LastSubscriptionAttributes!.PaymentCollectionMethod);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_ForAShopperWithNoBillingCustomer()
    {
        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEveryStateAndFlagsWhichAreStillCurrent()
    {
        var customer = _client.SeedCustomer(MaxioCustomerReference.For(_subscriber));
        _client.SeedSubscription(customer.Id, ProPlan, SubscriptionStates.Active);
        _client.SeedSubscription(customer.Id, BasicPlan, SubscriptionStates.Canceled);

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Equal(2, subscriptions.Count);
        Assert.True(subscriptions.Single(s => s.PlanHandle == ProPlan).IsCurrent);
        Assert.False(subscriptions.Single(s => s.PlanHandle == BasicPlan).IsCurrent);
    }

    [Fact]
    public async Task GetPlansAsync_ReportsMissingConfigurationRatherThanCallingMaxio()
    {
        _settings.ApiKey = null;

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => CreateService().GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    // The cache and the keyed lock are process-wide singletons in the host, so the tests share them
    // across the per-request service instances they create.
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly KeyedAsyncLock _locks = new();

    private MaxioSubscriptionBillingService CreateService() =>
        new(_client, _settings, _cache, _locks, NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static MaxioProduct NewProduct(string handle, string name, long priceInCents) => new()
    {
        Id = Random.Shared.Next(1, 100000),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Handle = ProFamily }
    };
}
