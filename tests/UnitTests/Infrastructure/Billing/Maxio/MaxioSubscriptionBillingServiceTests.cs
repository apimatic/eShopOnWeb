using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Covers the orchestration the service adds on top of the raw Maxio calls: plan resolution,
/// customer reuse, and the several ways a repeated subscribe has to collapse onto one
/// subscription.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "demo-family";
    private const string ProPlan = "eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly BillingIdentity _identity = new("demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product(ProPlan, "Pro Plan", 29_900),
                Product("basic-plan", "Basic Plan", 2_900),
                Product("retired-plan", "Retired Plan", 100, archived: true)
            });

        _client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSite { RelationshipInvoicingEnabled = true });
    }

    [Fact]
    public async Task ListPlansSkipsArchivedProductsAndOrdersByPrice()
    {
        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", ProPlan }, plans.Select(plan => plan.Handle));
        Assert.Equal(2_900, plans[0].PriceInCents);
        Assert.Equal("month", plans[0].IntervalUnit);
    }

    [Fact]
    public async Task ListPlansIsReadOnceAndThenServedFromCache()
    {
        var service = CreateService();

        await service.ListPlansAsync();
        await service.ListPlansAsync();

        await _client.Received(1).ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingRejectsAPlanOutsideTheConfiguredFamily()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(new SubscribeCommand(_identity, "some-other-product")));

        await _client.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingRefusesToRunWithoutConfiguration()
    {
        var service = CreateService(new MaxioSettings { Subdomain = "acme" });

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.SubscribeAsync(new SubscribeCommand(_identity, ProPlan)));
    }

    [Fact]
    public async Task SubscribingCreatesTheCustomerOnceAndThenEnrolsThem()
    {
        var customer = GivenNoCustomerYet();
        GivenNoSubscriptions(customer.Id);
        GivenSubscriptionIsCreated(99, "active");

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Equal(29_900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(created =>
                created.Reference == MaxioReference.ForCustomer(_identity.UserName) &&
                created.Email == _identity.Email &&
                !string.IsNullOrWhiteSpace(created.FirstName) &&
                !string.IsNullOrWhiteSpace(created.LastName)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingReusesAnExistingCustomer()
    {
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);
        GivenSubscriptionIsCreated(99, "active");

        await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        await _client.DidNotReceive().CreateCustomerAsync(
            Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARacedCustomerCreateFallsBackToTheOneThatWon()
    {
        var winner = Customer(7);

        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, winner);

        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateReference());

        GivenNoSubscriptions(winner.Id);
        GivenSubscriptionIsCreated(99, "active");

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        await _client.Received(1).ListCustomerSubscriptionsAsync(winner.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribingAgainReturnsTheLiveSubscriptionWithoutCreatingAnother()
    {
        var customer = GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(55, "active", ProPlan) });

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.False(result.Created);
        Assert.Equal(55, result.Subscription.Id);

        await _client.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASubscriptionToADifferentPlanDoesNotBlockThisOne()
    {
        var customer = GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(55, "active", "basic-plan") });

        GivenSubscriptionIsCreated(99, "active");

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task ACancelledSubscriptionDoesNotBlockANewOne()
    {
        var customer = GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(55, "canceled", ProPlan) });

        GivenSubscriptionIsCreated(99, "active");

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task ADuplicateReferenceResolvesToTheSubscriptionThatOwnsIt()
    {
        // Models the cross-instance race: our customer read saw no live subscription, but by the
        // time we created one another instance had already taken the reference.
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);

        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateReference());

        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(77, "active", ProPlan));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.IdempotentReplay, result.Outcome);
        Assert.Equal(77, result.Subscription.Id);
    }

    [Fact]
    public async Task AnExplicitIdempotencyKeyReplaysEvenWhenTheSubscriptionHasEnded()
    {
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);

        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateReference());

        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(77, "canceled", ProPlan));

        var result = await CreateService()
            .SubscribeAsync(new SubscribeCommand(_identity, ProPlan, idempotencyKey: "checkout-42"));

        Assert.Equal(SubscribeOutcome.IdempotentReplay, result.Outcome);
        Assert.Equal(77, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReSubscribingAfterCancellationMovesToAReferenceDerivedFromTheOldOne()
    {
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);

        var attempted = new List<string?>();
        var baseReference = MaxioReference.ForSubscription(
            MaxioReference.ForCustomer(_identity.UserName), ProPlan);

        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<MaxioCreateSubscription>();
                attempted.Add(request.Reference);

                if (request.Reference == baseReference)
                {
                    throw DuplicateReference();
                }

                return Task.FromResult(Subscription(101, "active", ProPlan));
            });

        _client.FindSubscriptionByReferenceAsync(baseReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(55, "canceled", ProPlan));

        var result = await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(101, result.Subscription.Id);
        Assert.Equal(new[] { baseReference, $"{baseReference}-r55" }, attempted);
    }

    [Fact]
    public async Task EnrolsWithRemittanceOnRelationshipInvoicingSitesSoNoCardIsNeeded()
    {
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);
        GivenSubscriptionIsCreated(99, "active");

        await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(request => request.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrolsWithInvoiceOnLegacySites()
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSite { RelationshipInvoicingEnabled = false });

        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);
        GivenSubscriptionIsCreated(99, "active");

        await CreateService().SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(request => request.PaymentCollectionMethod == "invoice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConfiguredCollectionMethodOverridesTheSiteLookup()
    {
        var customer = GivenExistingCustomer();
        GivenNoSubscriptions(customer.Id);
        GivenSubscriptionIsCreated(99, "active");

        var settings = ValidSettings();
        settings.PaymentCollectionMethod = "automatic";

        await CreateService(settings).SubscribeAsync(new SubscribeCommand(_identity, ProPlan));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(request => request.PaymentCollectionMethod == "automatic"),
            Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentSubscribesForOneShopperProduceOneSubscription()
    {
        var customer = GivenExistingCustomer();
        var created = 0;
        var stored = new List<MaxioSubscription>();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(_ => stored.ToList());

        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref created);
                var subscription = Subscription(500, "active", ProPlan);
                stored.Add(subscription);
                return Task.FromResult(subscription);
            });

        var service = CreateService();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(new SubscribeCommand(_identity, ProPlan))));

        Assert.Equal(1, created);
        Assert.Single(results.Where(result => result.Created));
        Assert.All(results, result => Assert.Equal(500, result.Subscription.Id));
    }

    [Fact]
    public async Task ListingSubscriptionsForAShopperWithNoCustomerReturnsNothing()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(_identity);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListingSubscriptionsPutsLiveOnesFirst()
    {
        var customer = GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(1, "canceled", ProPlan),
                Subscription(2, "active", "basic-plan")
            });

        var subscriptions = await CreateService().ListSubscriptionsAsync(_identity);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task TimestampsKeepTheOffsetMaxioReported()
    {
        var customer = GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(1, "active", ProPlan) });

        var subscription = (await CreateService().ListSubscriptionsAsync(_identity)).Single();

        Assert.Equal(TimeSpan.FromHours(5), subscription.NextBillingAt!.Value.Offset);
        Assert.Equal(new DateTime(2026, 10, 6, 10, 3, 57), subscription.NextBillingAt!.Value.DateTime);
    }

    private MaxioSubscriptionBillingService CreateService(MaxioSettings? settings = null) =>
        new(
            _client,
            new StaticOptionsMonitor(settings ?? ValidSettings()),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static MaxioSettings ValidSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = FamilyHandle
    };

    private MaxioCustomer GivenExistingCustomer()
    {
        var customer = Customer(7);

        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(customer);

        return customer;
    }

    private MaxioCustomer GivenNoCustomerYet()
    {
        var customer = Customer(7);

        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(customer);

        return customer;
    }

    private void GivenNoSubscriptions(long customerId) =>
        _client.ListCustomerSubscriptionsAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());

    private void GivenSubscriptionIsCreated(long id, string state) =>
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(id, state, ProPlan));

    private static BillingValidationException DuplicateReference() =>
        new(new[] { "Reference: must be unique - that value has been taken." });

    private static MaxioCustomer Customer(long id) => new()
    {
        Id = id,
        Reference = MaxioReference.ForCustomer("demouser@microsoft.com"),
        Email = "demouser@microsoft.com"
    };

    private static MaxioProduct Product(string handle, string name, long priceInCents, bool archived = false) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ArchivedAt = archived ? "2026-01-01T00:00:00+05:00" : null,
        ProductFamily = new MaxioProductFamily { Handle = FamilyHandle }
    };

    private static MaxioSubscription Subscription(long id, string state, string planHandle) => new()
    {
        Id = id,
        State = state,
        Currency = "USD",
        ProductPriceInCents = planHandle == ProPlan ? 29_900 : 2_900,
        CurrentPeriodStartedAt = "2026-09-06T10:03:57+05:00",
        CurrentPeriodEndsAt = "2026-10-06T10:03:57+05:00",
        NextAssessmentAt = "2026-10-06T10:03:57+05:00",
        ActivatedAt = "2026-09-06T10:03:58+05:00",
        PaymentCollectionMethod = "remittance",
        Customer = Customer(7),
        Product = Product(planHandle, planHandle, planHandle == ProPlan ? 29_900 : 2_900)
    };

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioSettings>
    {
        public StaticOptionsMonitor(MaxioSettings settings) => CurrentValue = settings;

        public MaxioSettings CurrentValue { get; }

        public MaxioSettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioSettings, string?> listener) => null;
    }
}
