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
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new Site { Currency = "USD", RelationshipInvoicingEnabled = true });

        _client.ListProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProductFamily> { new() { Id = 3026729, Handle = FamilyHandle, Name = "eShopSubscribe" } });

        _client.ListProductsForProductFamilyAsync(3026729, false, Arg.Any<CancellationToken>())
            .Returns(new List<Product>
            {
                Pro(),
                Basic(),
                new() { Id = 3, Handle = "retired-plan", Name = "Retired", PriceInCents = 100, ArchivedAt = DateTimeOffset.UtcNow }
            });

        _client.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
    }

    private static Product Pro() => new()
    {
        Id = 7130995,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false
    };

    private static Product Basic() => new()
    {
        Id = 7130996,
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceInCents = 2900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false
    };

    private MaxioSubscriptionBillingService CreateService(MaxioOptions? options = null) =>
        new(_client,
            new StaticOptionsMonitor(options ?? new MaxioOptions
            {
                ApiKey = "key",
                Subdomain = "acme",
                ProductFamilyHandle = FamilyHandle
            }),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    private void GivenExistingCustomer(int id = 42) =>
        _client.ReadCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = id, Reference = _subscriber.BillingReference, Email = _subscriber.Email });

    // -------------------------------------------------------------------------------------------------
    // Plans
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListPlans_ProjectsTheConfiguredFamilyAndDropsArchivedProducts()
    {
        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal(29900, plans.Single(plan => plan.Handle == "eshop-pro").PriceInCents);
        Assert.All(plans, plan => Assert.Equal("USD", plan.Currency));
        Assert.All(plans, plan => Assert.Equal(FamilyHandle, plan.ProductFamilyHandle));
    }

    [Fact]
    public async Task ListPlans_CachesTheCatalog()
    {
        var service = CreateService();

        await service.ListPlansAsync();
        await service.ListPlansAsync();

        await _client.Received(1).ListProductsForProductFamilyAsync(3026729, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListPlans_ReportsAMisconfiguredProductFamily()
    {
        _client.ListProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProductFamily> { new() { Id = 1, Handle = "something-else" } });

        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(() => CreateService().ListPlansAsync());

        Assert.Contains("something-else", exception.Message);
    }

    [Fact]
    public async Task ListPlans_IsUnavailableWhenTheIntegrationIsNotConfigured()
    {
        var service = CreateService(new MaxioOptions());

        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.ListPlansAsync());
    }

    // -------------------------------------------------------------------------------------------------
    // Subscribe
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Subscribe_CreatesTheBillingCustomerOnFirstUse()
    {
        _client.ReadCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 42, Reference = _subscriber.BillingReference });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active", Product = Pro() });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(900, result.Subscription.Id);
        Assert.True(result.Subscription.IsLive);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(customer =>
                customer.Reference == _subscriber.BillingReference &&
                customer.Email == _subscriber.Email &&
                customer.FirstName == "Demouser"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesAnExistingBillingCustomer()
    {
        GivenExistingCustomer();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active", Product = Pro() });

        await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_RecoversWhenTheCustomerIsCreatedConcurrently()
    {
        _client.ReadCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => new Customer { Id = 42, Reference = _subscriber.BillingReference });
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(ReferenceConflict());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active", Product = Pro() });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        await _client.Received(1).ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotentWhenALiveSubscriptionAlreadyExists()
    {
        GivenExistingCustomer();
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>
            {
                new() { Id = 900, State = "active", Product = Pro(), ProductPriceInCents = 29900 }
            });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(900, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IgnoresEndOfLifeSubscriptionsForTheSamePlan()
    {
        GivenExistingCustomer();
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { new() { Id = 800, State = "canceled", Product = Pro() } });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 901, State = "active", Product = Pro() });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);

        // The new enrolment must not reuse the reference of the subscription that ended.
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(request => request.Reference == $"{_subscriber.BillingReference}|eshop-pro|2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_SendsADeterministicReferenceAndAnInvoiceCollectionMethod()
    {
        GivenExistingCustomer();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active", Product = Pro() });

        await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(request =>
                request.ProductHandle == "eshop-pro" &&
                request.CustomerId == 42 &&
                request.Reference == $"{_subscriber.BillingReference}|eshop-pro|1" &&
                request.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_LeavesTheCollectionMethodToTheSiteWhenThePlanNeedsACard()
    {
        GivenExistingCustomer();
        _client.ListProductsForProductFamilyAsync(3026729, false, Arg.Any<CancellationToken>())
            .Returns(new List<Product> { new() { Id = 1, Handle = "card-plan", Name = "Card", PriceInCents = 100, RequireCreditCard = true } });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active" });

        await CreateService().SubscribeAsync(_subscriber, "card-plan");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(request => request.PaymentCollectionMethod == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_HonoursAConfiguredCollectionMethod()
    {
        GivenExistingCustomer();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new Subscription { Id = 900, State = "active", Product = Pro() });

        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle,
            PaymentCollectionMethod = "Automatic"
        };

        await CreateService(options).SubscribeAsync(_subscriber, "eshop-pro");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(request => request.PaymentCollectionMethod == "automatic"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReplaysTheWinningSubscriptionWhenTheReferenceIsAlreadyTaken()
    {
        GivenExistingCustomer();
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<Subscription>(),
                _ => new List<Subscription> { new() { Id = 900, State = "active", Product = Pro() } });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(ReferenceConflict());

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(900, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_RecoversTheSubscriptionWhenTheCreateResponseIsLost()
    {
        GivenExistingCustomer();
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<Subscription>(),
                _ => new List<Subscription>
                {
                    new()
                    {
                        Id = 900,
                        State = "active",
                        Product = Pro(),
                        Reference = $"{_subscriber.BillingReference}|eshop-pro|1"
                    }
                });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderUnavailableException("connection reset"));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(900, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_SurfacesTheFailureWhenNothingCanBeRecovered()
    {
        GivenExistingCustomer();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderUnavailableException("connection reset"));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => CreateService().SubscribeAsync(_subscriber, "eshop-pro"));
    }

    [Fact]
    public async Task Subscribe_RejectsAPlanThatIsNotInTheConfiguredCatalog()
    {
        GivenExistingCustomer();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, "not-a-plan"));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_SerialisesConcurrentAttemptsSoOnlyOneSubscriptionIsCreated()
    {
        var created = new List<Subscription>();
        GivenExistingCustomer();

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => created.ToList());

        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(20);
                var subscription = new Subscription { Id = 900 + created.Count, State = "active", Product = Pro() };
                created.Add(subscription);
                return subscription;
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(_subscriber, "eshop-pro")));

        Assert.Single(created);
        Assert.Single(results, result => result.Created);
        Assert.All(results, result => Assert.Equal(900, result.Subscription.Id));
    }

    [Fact]
    public async Task Subscribe_TranslatesProviderRejectionsIntoADomainFailure()
    {
        GivenExistingCustomer();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "subscriptions.json",
                new[] { "No payment method was on file for the $299.00 balance" },
                null));

        var exception = await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => CreateService().SubscribeAsync(_subscriber, "eshop-pro"));

        Assert.Equal("No payment method was on file for the $299.00 balance", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task Subscribe_TranslatesRejectedCredentialsIntoAConfigurationFailure()
    {
        _client.ReadCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.Unauthorized, "GET", "customers/lookup.json", Array.Empty<string>(), null));

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => CreateService().SubscribeAsync(_subscriber, "eshop-pro"));
    }

    // -------------------------------------------------------------------------------------------------
    // My subscriptions
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListSubscriptions_ReturnsAnEmptyListWithoutCreatingACustomer()
    {
        _client.ReadCustomerByReferenceAsync(_subscriber.BillingReference, Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(_subscriber);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptions_MapsBillingFieldsAndPutsLiveSubscriptionsFirst()
    {
        GivenExistingCustomer();
        var nextBilling = DateTimeOffset.Parse("2026-10-06T22:33:27+05:00");

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>
            {
                new() { Id = 800, State = "canceled", Product = Basic(), ActivatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
                new()
                {
                    Id = 900,
                    State = "active",
                    Product = Pro(),
                    ProductPriceInCents = 29900,
                    Currency = "USD",
                    NextAssessmentAt = nextBilling,
                    PaymentCollectionMethod = "remittance",
                    BalanceInCents = 29900,
                    ActivatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                }
            });

        var subscriptions = await CreateService().ListSubscriptionsAsync(_subscriber);

        var live = subscriptions.First();
        Assert.Equal(900, live.Id);
        Assert.True(live.IsLive);
        Assert.Equal("eshop-pro", live.PlanHandle);
        Assert.Equal("Pro Plan", live.PlanName);
        Assert.Equal(29900, live.PriceInCents);
        Assert.Equal("USD", live.Currency);
        Assert.Equal(1, live.Interval);
        Assert.Equal("month", live.IntervalUnit);
        Assert.Equal(nextBilling, live.NextBillingAt);
        Assert.Equal("remittance", live.PaymentCollectionMethod);
        Assert.Equal(42, live.BillingCustomerId);
        Assert.Equal(_subscriber.BillingReference, live.BillingCustomerReference);

        Assert.False(subscriptions.Last().IsLive);
    }

    private static MaxioApiException ReferenceConflict() => new(
        HttpStatusCode.UnprocessableEntity,
        "POST",
        "customers.json",
        new[] { "Reference: must be unique - that value has been taken." },
        null);

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioOptions>
    {
        public StaticOptionsMonitor(MaxioOptions options) => CurrentValue = options;

        public MaxioOptions CurrentValue { get; }

        public MaxioOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioOptions, string?> listener) => null;
    }
}
