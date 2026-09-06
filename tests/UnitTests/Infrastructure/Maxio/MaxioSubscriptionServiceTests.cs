using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string CustomerReference = "eshoponweb-demouser@microsoft.com";
    private const string ProSubscriptionReference = CustomerReference + ":eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly Subscriber _subscriber = new(
        userId: "user-1", userName: "demouser@microsoft.com", email: "demouser@microsoft.com");

    private MaxioSubscriptionService CreateService(MaxioSettings? settings = null) => new(
        _client,
        Options.Create(settings ?? new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle,
            // Caching would make the assertions on call counts depend on test ordering.
            CatalogCacheSeconds = 0
        }),
        new KeyedAsyncLock(),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<MaxioSubscriptionService>.Instance);

    private void GivenCatalog(params MaxioProduct[] products)
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { Currency = "USD" });
        _client.ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioProduct>)products.ToList());
    }

    private static MaxioProduct Product(
        string handle,
        string name,
        long priceInCents,
        bool requireCreditCard = false,
        DateTimeOffset? archivedAt = null) => new()
        {
            Id = handle.GetHashCode(),
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = requireCreditCard,
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Handle = FamilyHandle }
        };

    private static MaxioSubscription Subscription(
        long id,
        string state,
        string planHandle,
        long customerId = 42,
        string? reference = null) => new()
        {
            Id = id,
            State = state,
            Reference = reference,
            Currency = "USD",
            ProductPriceInCents = 29900,
            NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
            Product = new MaxioProduct { Handle = planHandle, Name = "Pro Plan", Interval = 1, IntervalUnit = "month" },
            Customer = new MaxioCustomer { Id = customerId, Reference = CustomerReference }
        };

    private static MaxioApiException DuplicateReference() => new(
        HttpMethod.Post,
        "subscriptions.json",
        HttpStatusCode.UnprocessableEntity,
        new[] { "Reference: must be unique - that value has been taken." },
        rawBody: null);

    private static MaxioApiException Unprocessable(string message) => new(
        HttpMethod.Post, "subscriptions.json", HttpStatusCode.UnprocessableEntity, new[] { message }, rawBody: null);

    // ---- Plans -------------------------------------------------------------------------------

    [Fact]
    public async Task ListPlansSkipsArchivedProductsAndOrdersByPrice()
    {
        GivenCatalog(
            Product("eshop-pro", "Pro Plan", 29900),
            Product("basic-plan", "Basic Plan", 2900),
            Product("legacy", "Legacy Plan", 100, archivedAt: DateTimeOffset.UtcNow));

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal(29m, plans[0].Price);
        Assert.All(plans, plan => Assert.Equal("USD", plan.Currency));
    }

    [Fact]
    public async Task ListPlansCachesTheCatalogWhenCachingIsEnabled()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        var service = CreateService(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle,
            CatalogCacheSeconds = 60
        });

        await service.ListPlansAsync();
        await service.ListPlansAsync();

        await _client.Received(1).ListProductsForFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>());
    }

    // ---- Subscribe ---------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeCreatesTheCustomerOnFirstUseAndThenTheSubscription()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, SubscriptionStates.Active, "eshop-pro", reference: ProSubscriptionReference));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(SubscriptionStates.Active, result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(customer =>
                customer.Reference == CustomerReference &&
                customer.Email == "demouser@microsoft.com" &&
                customer.FirstName.Length > 0 &&
                customer.LastName.Length > 0),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(subscription =>
                subscription.ProductHandle == "eshop-pro" &&
                subscription.CustomerId == 42 &&
                subscription.Reference == ProSubscriptionReference &&
                subscription.PaymentCollectionMethod == MaxioCollectionMethods.Remittance),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesAnExistingCustomer()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, SubscriptionStates.Active, "eshop-pro"));

        await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsTheExistingLiveSubscriptionInsteadOfCreatingASecondOne()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>
            {
                Subscription(7, SubscriptionStates.Active, "eshop-pro", reference: ProSubscriptionReference)
            });

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIgnoresSubscriptionsToOtherPlansAndTerminatedOnes()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900), Product("basic-plan", "Basic Plan", 2900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>
            {
                Subscription(5, SubscriptionStates.Active, "basic-plan"),
                Subscription(6, SubscriptionStates.Canceled, "eshop-pro")
            });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(9, SubscriptionStates.Active, "eshop-pro"));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(9, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeRecoversWhenAnotherRequestCreatedTheCustomerFirst()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        var raced = new MaxioCustomer { Id = 42, Reference = CustomerReference };
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, raced);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(
                HttpMethod.Post, "customers.json", HttpStatusCode.UnprocessableEntity,
                new[] { "Reference: must be unique - that value has been taken." }, rawBody: null));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, SubscriptionStates.Active, "eshop-pro"));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        await _client.Received(2).FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsTheWinnerWhenAnotherRequestAlreadyTookTheSubscriptionReference()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DuplicateReference());
        _client.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(7, SubscriptionStates.Active, "eshop-pro", reference: ProSubscriptionReference));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeMintsAFreshReferenceWhenTheStableOneIsHeldByATerminatedSubscription()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(7, SubscriptionStates.Canceled, "eshop-pro", reference: ProSubscriptionReference));

        var attempts = 0;
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1
                ? Task.FromException<MaxioSubscription>(DuplicateReference())
                : Task.FromResult(Subscription(11, SubscriptionStates.Active, "eshop-pro")));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(11, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(subscription =>
                subscription.Reference != null &&
                subscription.Reference.StartsWith(ProSubscriptionReference + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeDoesNotReuseASubscriptionReferenceOwnedByADifferentCustomer()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(7, SubscriptionStates.Active, "eshop-pro", customerId: 99));

        var attempts = 0;
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1
                ? Task.FromException<MaxioSubscription>(DuplicateReference())
                : Task.FromResult(Subscription(12, SubscriptionStates.Active, "eshop-pro")));

        var result = await CreateService().SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(12, result.Subscription.Id);
    }

    [Fact]
    public async Task ConcurrentSubscribeCallsForOneShopperProduceOneSubscription()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        MaxioCustomer? customer = null;
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(customer));
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(customer = new MaxioCustomer { Id = 42, Reference = CustomerReference }));

        MaxioSubscription? created = null;
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<MaxioSubscription>>(
                created is null ? new List<MaxioSubscription>() : new List<MaxioSubscription> { created }));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                created = Subscription(7, SubscriptionStates.Active, "eshop-pro", reference: ProSubscriptionReference)));

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(_subscriber, "eshop-pro")));

        Assert.Single(results.Where(result => result.Created));
        Assert.All(results, result => Assert.Equal(7, result.Subscription.Id));
        await _client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeUsesTheConfiguredDefaultPlanWhenNoneIsRequested()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900), Product("basic-plan", "Basic Plan", 2900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, SubscriptionStates.Active, "eshop-pro"));

        var service = CreateService(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = FamilyHandle,
            DefaultPlanHandle = "eshop-pro",
            CatalogCacheSeconds = 0
        });

        await service.SubscribeAsync(_subscriber, planHandle: null);

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(subscription => subscription.ProductHandle == "eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRefusesToGuessAPlanWhenNoneIsRequestedOrConfigured()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        await Assert.ThrowsAsync<SubscriptionPlanRequiredException>(
            () => CreateService().SubscribeAsync(_subscriber, planHandle: null));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRejectsAnUnknownPlan()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_subscriber, "no-such-plan"));

        Assert.Contains("eshop-pro", exception.Message);
    }

    [Fact]
    public async Task SubscribeRejectsAPlanThatWouldNeedACardCaptured()
    {
        GivenCatalog(Product("card-plan", "Card Plan", 1000, requireCreditCard: true));

        await Assert.ThrowsAsync<PaymentMethodRequiredException>(
            () => CreateService().SubscribeAsync(_subscriber, "card-plan"));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeSurfacesOtherMaxioFailuresAsProviderErrors()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Unprocessable("No payment method was on file for the $299.00 balance"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => CreateService().SubscribeAsync(_subscriber, "eshop-pro"));

        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.ProviderErrors);
    }

    // ---- My subscriptions --------------------------------------------------------------------

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAShopperWhoNeverSubscribed()
    {
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        Assert.Empty(await CreateService().ListSubscriptionsAsync(_subscriber));

        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsPutsLiveSubscriptionsFirst()
    {
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = CustomerReference });

        var canceled = Subscription(1, SubscriptionStates.Canceled, "basic-plan");
        canceled.ActivatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var active = Subscription(2, SubscriptionStates.Active, "eshop-pro");
        active.ActivatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MaxioSubscription>)new List<MaxioSubscription> { canceled, active });

        var subscriptions = await CreateService().ListSubscriptionsAsync(_subscriber);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }
}
