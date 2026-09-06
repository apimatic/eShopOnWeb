using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Covers the orchestration this integration owns: making the subscribe flow idempotent, keeping
/// subscriptions inside the configured catalog, and translating Maxio failures into terms the API
/// layer can act on.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string PlanHandle = "eshop-pro";
    private const string UserName = "demouser@microsoft.com";
    private const string CustomerReference = "eshoponweb-demouser@microsoft.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly KeyedAsyncLock _lock = new();

    private readonly SubscriberIdentity _subscriber = new(UserName, UserName);

    private MaxioSubscriptionBillingService CreateService(MaxioOptions? options = null) => new(
        _client,
        new StaticOptionsMonitor<MaxioOptions>(options ?? DefaultOptions()),
        _cache,
        _lock,
        NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static MaxioOptions DefaultOptions() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = FamilyHandle,
        CatalogCacheSeconds = 0
    };

    private static MaxioProduct Plan(string handle = PlanHandle, string family = FamilyHandle) => new()
    {
        Id = 1,
        Handle = handle,
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = family }
    };

    private static MaxioCustomer Customer(long id = 42) => new()
    {
        Id = id,
        Reference = CustomerReference,
        Email = UserName
    };

    private static MaxioSubscription Subscription(
        long id = 100,
        string state = "active",
        string planHandle = PlanHandle,
        string? reference = null) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        Currency = "USD",
        NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
        Product = Plan(planHandle),
        Customer = Customer(),
        Reference = reference ?? $"{CustomerReference}:{planHandle}"
    };

    [Fact]
    public async Task SubscribeCreatesTheCustomerAndTheSubscriptionOnAColdStart()
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        Assert.Equal(100, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(c => c.Reference == CustomerReference && c.Email == UserName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIdentifiesTheCustomerByIdAndStampsADeterministicReference()
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(s =>
                s.CustomerId == 42 &&
                s.ProductHandle == PlanHandle &&
                s.PaymentCollectionMethod == "remittance" &&
                s.Reference == $"{CustomerReference}:{PlanHandle}"),
            Arg.Any<CancellationToken>());

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingEnrollmentInsteadOfCreatingASecond()
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { Subscription(id: 777) });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("awaiting_signup")]
    public async Task SubscribeTreatsEveryNonTerminalStateAsAlreadySubscribed(string state)
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { Subscription(state: state) });

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAllowsReSubscribingAfterCancellationUnderAFreshReference()
    {
        // The cancelled subscription keeps its reference forever, and Maxio enforces uniqueness on
        // it, so re-enrolling has to pick a different one.
        var cancelled = Subscription(id: 500, state: "canceled");

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { cancelled });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription(id: 501));

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(s => s.Reference == $"{CustomerReference}:{PlanHandle}:2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesTheCustomerWhenAConcurrentRequestCreatedItFirst()
    {
        var duplicate = new MaxioApiException(
            HttpStatusCode.UnprocessableEntity,
            "POST",
            "customers.json",
            new[] { "Reference: must be unique - that value has been taken." });

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer());
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>()).Throws(duplicate);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.Created);
        await _client.Received(2).FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeResolvesToTheWinningSubscriptionWhenMaxioRejectsADuplicateReference()
    {
        var duplicate = new MaxioApiException(
            HttpStatusCode.UnprocessableEntity,
            "POST",
            "subscriptions.json",
            new[] { "Reference: must be unique - that value has been taken." });

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>(), new[] { Subscription(id: 999) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Throws(duplicate);

        var result = await CreateService().SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.Created);
        Assert.Equal(999, result.Subscription.Id);
    }

    [Fact]
    public async Task ConcurrentSubscribesCreateExactlyOneSubscription()
    {
        var created = 0;

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => Volatile.Read(ref created) == 0
                ? Array.Empty<MaxioSubscription>()
                : new[] { Subscription(id: 314) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref created);
                return Task.FromResult(Subscription(id: 314));
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => service.SubscribeAsync(_subscriber, PlanHandle))));

        Assert.Equal(1, Volatile.Read(ref created));
        Assert.Single(results, r => r.Created);
        Assert.All(results, r => Assert.Equal(314, r.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeRefusesAPlanFromAnotherProductFamily()
    {
        _client.ReadProductByHandleAsync("other-plan", Arg.Any<CancellationToken>())
            .Returns(Plan("other-plan", family: "someone-elses-catalog"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, "other-plan"));

        Assert.Equal(BillingErrorKind.NotFound, ex.Kind);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRefusesAnUnknownPlanWithoutTouchingTheCustomerRecord()
    {
        _client.ReadProductByHandleAsync("nope", Arg.Any<CancellationToken>()).Returns((MaxioProduct?)null);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, "nope"));

        Assert.Equal(BillingErrorKind.NotFound, ex.Kind);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRefusesAnArchivedPlan()
    {
        var archived = Plan();
        archived.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(archived);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, PlanHandle));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task SubscribeRequiresAPlanHandleWhenNoDefaultIsConfigured()
    {
        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, null));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task SubscribeFallsBackToTheConfiguredDefaultPlan()
    {
        var options = DefaultOptions();
        options.DefaultPlanHandle = PlanHandle;

        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>()).Returns(Plan());
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(Subscription());

        var result = await CreateService(options).SubscribeAsync(_subscriber, null);

        Assert.True(result.Created);
    }

    [Fact]
    public async Task RejectedCredentialsSurfaceAsAConfigurationFailureRatherThanACallerError()
    {
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.Unauthorized, "GET", "products/handle/eshop-pro.json"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, PlanHandle));

        Assert.Equal(BillingErrorKind.Configuration, ex.Kind);
    }

    [Fact]
    public async Task SiteDataClearingIsReportedAsTemporarilyUnavailableNotAsABadRequest()
    {
        // Maxio answers reads with 422 while a test site is being cleared. The spec describes
        // clearing as asynchronous, so the right advice to the caller is "retry", not "fix your input".
        _client.ReadProductByHandleAsync(PlanHandle, Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "GET",
                "products/handle/eshop-pro.json",
                new[] { "Site data clearing is in progress. Please try later." }));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => CreateService().SubscribeAsync(_subscriber, PlanHandle));

        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task MissingConfigurationIsReportedBeforeAnyCallIsMade()
    {
        var service = CreateService(new MaxioOptions());

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.ListPlansAsync());

        Assert.Equal(BillingErrorKind.Configuration, ex.Kind);
        Assert.Contains(ex.Errors, e => e.Contains("Maxio:ApiKey"));
        await _client.DidNotReceive().ListProductsInFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAShopperWhoHasNeverSubscribed()
    {
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReturnsTheShoppersOwnEnrollmentsNewestFirst()
    {
        var older = Subscription(id: 1, planHandle: "basic-plan");
        older.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = Subscription(id: 2);
        newer.CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { older, newer });

        var subscriptions = await CreateService().ListSubscriptionsAsync(_subscriber);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(s => s.Id).ToArray());
        Assert.Equal(CustomerReference, subscriptions[0].CustomerReference);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedPlansAndOrdersByPrice()
    {
        var archived = Plan("legacy");
        archived.ArchivedAt = DateTimeOffset.UtcNow.AddYears(-1);

        var cheap = Plan("basic-plan");
        cheap.PriceInCents = 2900;

        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
        _client.ListProductsInFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[] { Plan(), archived, cheap });

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", PlanHandle }, plans.Select(p => p.Handle).ToArray());
        Assert.All(plans, p => Assert.Equal("USD", p.Currency));
        Assert.Equal(29m, plans[0].Price);
    }

    [Fact]
    public async Task ListPlansReportsAnUnknownProductFamilyAsAConfigurationProblem()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
        _client.ListProductsInFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.NotFound, "GET", "product_families/handle:eshop-subscribe/products.json"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => CreateService().ListPlansAsync());

        Assert.Equal(BillingErrorKind.Configuration, ex.Kind);
        Assert.Contains("ProductFamilyHandle", ex.Message);
    }

    [Fact]
    public async Task ListPlansStillWorksWhenTheSiteCurrencyCannotBeRead()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.InternalServerError, "GET", "site.json"));
        _client.ListProductsInFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>()).Returns(new[] { Plan() });

        var plans = await CreateService().ListPlansAsync();

        Assert.Single(plans);
        Assert.Null(plans[0].Currency);
    }
}
