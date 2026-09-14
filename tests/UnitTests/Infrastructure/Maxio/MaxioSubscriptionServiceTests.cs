using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

public class MaxioSubscriptionServiceTests
{
    private static readonly Subscriber Shopper =
        new("DEMOUSER@MICROSOFT.COM", "demouser@microsoft.com", "Demo", "User");

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    public MaxioSubscriptionServiceTests()
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSite { Currency = "USD", Subdomain = "acme", Test = true });

        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", "Pro Plan", 29900), Product("basic-plan", "Basic Plan", 2900) });
    }

    [Fact]
    public async Task ListPlansMapsAndOrdersByPrice()
    {
        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal(299m, plans[1].Price);
        Assert.Equal("USD", plans[1].Currency);
        Assert.Equal("month", plans[1].IntervalUnit);
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        var archived = Product("legacy-plan", "Legacy Plan", 100);
        archived.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);

        _client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", "Pro Plan", 29900), archived });

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "eshop-pro" }, plans.Select(plan => plan.Handle));
    }

    [Fact]
    public async Task ListPlansIsServedFromCacheOnTheSecondCall()
    {
        var service = CreateService();

        await service.ListPlansAsync();
        await service.ListPlansAsync();

        await _client.Received(1).ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeCreatesTheCustomerOnFirstEnrolment()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "eshoponweb-demouser@microsoft.com" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(7, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);

        var created = _client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateCustomerAsync))
            .Select(call => (CreateMaxioCustomerRequest)call.GetArguments()[0]!)
            .Single();

        Assert.Equal("eshoponweb-demouser@microsoft.com", created.Customer.Reference);
        Assert.Equal("demouser@microsoft.com", created.Customer.Email);

        // No uniqueness_token: the reference is enforced as unique for the life of the site, which is
        // a permanent guard rather than one that expires after 60 minutes.
        Assert.Null(created.UniquenessToken);
    }

    [Fact]
    public async Task SubscribeReusesAnExistingCustomer()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeSendsTheConfiguredPaymentCollectionMethodAndADeterministicReference()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        var request = CapturedSubscriptionRequest();
        Assert.Equal("eshop-pro", request.Subscription.ProductHandle);
        Assert.Equal(42, request.Subscription.CustomerId);
        Assert.Equal("remittance", request.Subscription.PaymentCollectionMethod);
        Assert.Equal("eshoponweb-demouser@microsoft.com-eshop-pro", request.Subscription.Reference);
        Assert.False(string.IsNullOrWhiteSpace(request.UniquenessToken));
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenTheShopperAlreadyHasALiveSubscription()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, "active", "eshop-pro") });

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(7, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APastDueSubscriptionStillCountsAsEnrolled()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, "past_due", "eshop-pro") });

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockSigningUpAgain()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, "canceled", "eshop-pro") });
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(8, result.Subscription.Id);

        // The second enrolment must not reuse the first one's reference or uniqueness token.
        Assert.Equal("eshoponweb-demouser@microsoft.com-eshop-pro-2", CapturedSubscriptionRequest().Subscription.Reference);
    }

    [Fact]
    public async Task ASubscriptionToAnotherPlanDoesNotSatisfyThisRequest()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(7, "active", "basic-plan") });
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(8, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal("eshop-pro", CapturedSubscriptionRequest().Subscription.ProductHandle);
    }

    [Fact]
    public async Task TheSubscriptionUniquenessTokenIsStableWithinTheIdempotencyWindow()
    {
        var tokens = new List<string?>();

        foreach (var offsetSeconds in new[] { 0, 30 })
        {
            var client = Substitute.For<IMaxioApiClient>();
            client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
            client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new[] { Product("eshop-pro", "Pro Plan", 29900) });
            client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new MaxioCustomer { Id = 42 });
            client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
            client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
                .Returns(Subscription(7, "active", "eshop-pro"));

            tokens.Add(await TokenFor(client, offsetSeconds));
        }

        Assert.Equal(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task TheSubscriptionUniquenessTokenRollsOverOnceTheWindowHasPassed()
    {
        var tokens = new List<string?>();

        foreach (var offsetSeconds in new[] { 0, 600 })
        {
            var client = Substitute.For<IMaxioApiClient>();
            client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
            client.ListProductsForFamilyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new[] { Product("eshop-pro", "Pro Plan", 29900) });
            client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new MaxioCustomer { Id = 42 });
            client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
            client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
                .Returns(Subscription(7, "active", "eshop-pro"));

            tokens.Add(await TokenFor(client, offsetSeconds));
        }

        // Maxio consumes the token even for a rejected attempt, so a shopper who fixes the cause must
        // not stay locked out for the full 60 minutes of Maxio's own window.
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task AnExplicitIdempotencyKeyTakesPrecedenceOverTheDerivedOne()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(Shopper, "eshop-pro");
        var derived = CapturedSubscriptionRequest().UniquenessToken;

        await CreateService().SubscribeAsync(Shopper, "eshop-pro", idempotencyKey: "caller-key-1");
        var supplied = CapturedSubscriptionRequest().UniquenessToken;

        Assert.NotEqual(derived, supplied);
    }

    [Fact]
    public async Task ADuplicateSubmissionIsResolvedByRereadingTheShopperState()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                _ => (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>(),
                _ => new[] { Subscription(7, "active", "eshop-pro") });
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.Conflict,
                "POST",
                "subscriptions.json",
                new[] { "DuplicatePrevention::DuplicateSubmissionError" }));

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task ADuplicateSubmissionWithNothingToShowForItSurfacesAsAConflict()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.Conflict,
                "POST",
                "subscriptions.json",
                new[] { "DuplicatePrevention::DuplicateSubmissionError" }));

        await Assert.ThrowsAsync<BillingConflictException>(() => CreateService().SubscribeAsync(Shopper, "eshop-pro"));
    }

    [Fact]
    public async Task ACustomerReferenceRaceConvergesOnTheWinningCustomer()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => (MaxioCustomer?)null,
                _ => new MaxioCustomer { Id = 42, Reference = "eshoponweb-demouser@microsoft.com" });
        _client.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "customers.json",
                new[] { "Reference: must be unique - that value has been taken." }));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribingToAnUnknownPlanIsRejectedBeforeAnyWrite()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(Shopper, "no-such-plan"));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OmittingThePlanHandleFallsBackToTheConfiguredDefault()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        var settings = Settings();
        settings.DefaultPlanHandle = "eshop-pro";

        await CreateService(settings).SubscribeAsync(Shopper, planHandle: null);

        Assert.Equal("eshop-pro", CapturedSubscriptionRequest().Subscription.ProductHandle);
    }

    [Fact]
    public async Task OmittingThePlanHandleWithNoDefaultAsksTheCallerToChooseOne()
    {
        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().SubscribeAsync(Shopper, planHandle: null));

        Assert.Contains("basic-plan", exception.Errors);
        Assert.Contains("eshop-pro", exception.Errors);
    }

    [Fact]
    public async Task AProviderRejectionIsTranslatedIntoAValidationFailure()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "subscriptions.json",
                new[] { "No payment method was on file for the $299.00 balance" }));

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Contains("No payment method was on file", exception.Message);
    }

    [Fact]
    public async Task AnUnreachableProviderIsTranslatedIntoAnUnavailableFailure()
    {
        GivenCustomer(42);
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioTransportException("boom", new TimeoutException()));

        await Assert.ThrowsAsync<BillingUnavailableException>(
            () => CreateService().SubscribeAsync(Shopper, "eshop-pro"));
    }

    [Fact]
    public async Task ListSubscriptionsIsEmptyForAShopperWhoNeverSubscribed()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        Assert.Empty(await CreateService().ListSubscriptionsAsync(Shopper));
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNewestFirstWithBillingDatesResolved()
    {
        GivenCustomer(42);

        var older = Subscription(7, "canceled", "basic-plan");
        older.CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var newer = Subscription(8, "active", "eshop-pro");
        newer.CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z");

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { older, newer });

        var subscriptions = await CreateService().ListSubscriptionsAsync(Shopper);

        Assert.Equal(new long[] { 8, 7 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
        Assert.Equal(newer.NextAssessmentAt, subscriptions[0].NextBillingAt);
    }

    [Fact]
    public async Task NextBillingFallsBackToThePeriodEndWhenNoAssessmentIsScheduled()
    {
        GivenCustomer(42);

        var subscription = Subscription(7, "active", "eshop-pro");
        subscription.NextAssessmentAt = null;

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { subscription });

        var subscriptions = await CreateService().ListSubscriptionsAsync(Shopper);

        Assert.Equal(subscription.CurrentPeriodEndsAt, subscriptions[0].NextBillingAt);
    }

    [Fact]
    public async Task EveryOperationRefusesToRunWithoutConfiguration()
    {
        var service = CreateService(new MaxioSettings());

        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.ListPlansAsync());
        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));
        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.ListSubscriptionsAsync(Shopper));
    }

    [Fact]
    public async Task MissingConfigurationNamesTheKeysThatAreMissing()
    {
        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => CreateService(new MaxioSettings()).ListPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    /// <summary>
    /// Runs one subscribe against <paramref name="client"/> with the clock advanced by
    /// <paramref name="offsetSeconds"/>, and returns the uniqueness token it sent.
    /// </summary>
    private static async Task<string?> TokenFor(IMaxioApiClient client, int offsetSeconds)
    {
        var service = new MaxioSubscriptionService(
            client,
            new TestOptionsMonitor<MaxioSettings>(Settings()),
            new MaxioReferenceFactory("eshoponweb"),
            new KeyedAsyncLock(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-09-06T12:00:00Z").AddSeconds(offsetSeconds)),
            NullLogger<MaxioSubscriptionService>.Instance);

        await service.SubscribeAsync(Shopper, "eshop-pro");

        return client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .Select(call => ((CreateMaxioSubscriptionRequest)call.GetArguments()[0]!).UniquenessToken)
            .Single();
    }

    private void GivenCustomer(long id) =>
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = id, Reference = "eshoponweb-demouser@microsoft.com" });

    private CreateMaxioSubscriptionRequest CapturedSubscriptionRequest() =>
        _client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .Select(call => (CreateMaxioSubscriptionRequest)call.GetArguments()[0]!)
            .Last();

    private static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioSubscriptionService CreateService(MaxioSettings? settings = null, TimeProvider? timeProvider = null) => new(
        _client,
        new TestOptionsMonitor<MaxioSettings>(settings ?? Settings()),
        new MaxioReferenceFactory("eshoponweb"),
        new KeyedAsyncLock(),
        new MemoryCache(new MemoryCacheOptions()),
        timeProvider ?? new FixedTimeProvider(DateTimeOffset.Parse("2026-09-06T12:00:00Z")),
        NullLogger<MaxioSubscriptionService>.Instance);

    /// <summary>A clock that does not move, so uniqueness tokens are reproducible within a test.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static MaxioProduct Product(string handle, string name, long priceInCents) => new()
    {
        Id = Random.Shared.Next(1, int.MaxValue),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe", Name = "eShopSubscribe" }
    };

    private static MaxioSubscription Subscription(long id, string state, string planHandle) => new()
    {
        Id = id,
        State = state,
        Currency = "USD",
        ProductPriceInCents = 29900,
        PaymentCollectionMethod = "remittance",
        CreatedAt = DateTimeOffset.UtcNow,
        CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
        CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
        NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
        Product = Product(planHandle, planHandle, 29900),
        Customer = new MaxioCustomer { Id = 42, Reference = "eshoponweb-demouser@microsoft.com" }
    };
}
