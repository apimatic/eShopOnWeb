using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly Subscriber _subscriber = new("user-1", "demouser@microsoft.com");

    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe",
        PlanCacheSeconds = 0
    };

    private MaxioSubscriptionService CreateService() => new(
        _client,
        new MemoryCache(new MemoryCacheOptions()),
        new StaticOptionsMonitor<MaxioSettings>(_settings),
        NullLogger<MaxioSubscriptionService>.Instance);

    private static MaxioProduct Product(string handle, string name, long priceInCents, bool requireCreditCard = false, DateTimeOffset? archivedAt = null) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = requireCreditCard,
        ArchivedAt = archivedAt,
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe", Name = "eShopSubscribe" }
    };

    private static MaxioSubscription Subscription(long id, string state, string planHandle, string? reference = null) => new()
    {
        Id = id,
        State = state,
        Reference = reference,
        ProductPriceInCents = 29900,
        NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
        Customer = new MaxioCustomer { Id = 42, Reference = "eshoponweb:demouser@microsoft.com", Email = "demouser@microsoft.com" },
        Product = new MaxioProduct { Handle = planHandle, Name = "Pro Plan", Interval = 1, IntervalUnit = "month" }
    };

    private void GivenCatalog(params MaxioProduct[] products) =>
        _client.ListProductsForProductFamilyAsync("handle:eshop-subscribe", Arg.Any<int>(), Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(products);

    private void GivenSite(bool relationshipInvoicing = true) =>
        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite { RelationshipInvoicingEnabled = relationshipInvoicing });

    private void GivenCustomer(MaxioCustomer? customer) =>
        _client.ReadCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>()).Returns(customer);

    private void GivenCustomerSubscriptions(params MaxioSubscription[] subscriptions) =>
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(subscriptions);

    [Fact]
    public async Task GetPlansReturnsActivePlansOrderedByPrice()
    {
        GivenCatalog(
            Product("eshop-pro", "Pro Plan", 29900),
            Product("basic-plan", "Basic Plan", 2900),
            Product("retired", "Retired Plan", 100, archivedAt: DateTimeOffset.UtcNow));

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal(299m, plans[1].Price);
        Assert.Equal("month", plans[1].BillingPeriod);
        Assert.Equal("eshop-subscribe", plans[1].ProductFamilyHandle);
    }

    [Fact]
    public async Task GetPlansListsWholeSiteWhenNoProductFamilyConfigured()
    {
        _settings.ProductFamilyHandle = null;
        _client.ListProductsAsync(Arg.Any<int>(), Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new[] { Product("eshop-pro", "Pro Plan", 29900) });

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
        await _client.DidNotReceive().ListProductsForProductFamilyAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnFirstUse()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference, Email = _subscriber.Email });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(customer =>
                customer.Reference == "eshoponweb:demouser@microsoft.com" &&
                customer.Email == "demouser@microsoft.com" &&
                !string.IsNullOrWhiteSpace(customer.FirstName) &&
                !string.IsNullOrWhiteSpace(customer.LastName)),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription =>
                subscription.ProductHandle == "eshop-pro" &&
                subscription.CustomerId == 42 &&
                subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesExistingCustomer()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReturnsExistingLiveSubscriptionInsteadOfEnrollingTwice()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions(Subscription(11, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(11, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("trial_ended")]
    [InlineData("failed_to_create")]
    public async Task SubscribeEnrollsAgainWhenPreviousSubscriptionEnded(string endedState)
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions(Subscription(11, endedState, "eshop-pro"));
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(12, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(12, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeDoesNotConfuseADifferentPlan()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900), Product("basic-plan", "Basic Plan", 2900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions(Subscription(11, "active", "basic-plan"));
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(12, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        Assert.True(result.Created);
    }

    [Fact]
    public async Task SubscribeReplaysIdempotencyKeyWithoutCreatingAnything()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(11, "active", "eshop-pro", reference: "eshoponweb-abc"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro", idempotencyKey: "click-1"));

        Assert.False(result.Created);
        Assert.Equal(11, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeStampsADeterministicReferenceWhenAnIdempotencyKeyIsGiven()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro", idempotencyKey: "click-1"));
        var firstReference = _client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .Select(call => ((CreateSubscription)call.GetArguments()[0]!).Reference)
            .Single();

        Assert.NotNull(firstReference);
        Assert.StartsWith("eshoponweb-", firstReference);

        // The same key for the same shopper always resolves to the same reference.
        _client.ClearReceivedCalls();
        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro", idempotencyKey: "click-1"));
        var secondReference = _client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .Select(call => ((CreateSubscription)call.GetArguments()[0]!).Reference)
            .Single();

        Assert.Equal(firstReference, secondReference);
    }

    [Fact]
    public async Task SubscribeRecoversWhenAConcurrentCallerCreatedTheCustomerFirst()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        var winner = new MaxioCustomer { Id = 42, Reference = _subscriber.Reference };
        _client.ReadCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => winner);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "POST", "customers.json",
                new[] { "Reference: must be unique." }, null));
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        Assert.True(result.Created);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription => subscription.CustomerId == 42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeLeavesCollectionMethodToTheSiteWhenThePlanNeedsAPaymentMethod()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900, requireCreditCard: true));
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription => subscription.PaymentCollectionMethod == null), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().ReadSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeUsesInvoiceCollectionOnLegacyStatementsSites()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite(relationshipInvoicing: false);
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription => subscription.PaymentCollectionMethod == "invoice"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeHonoursAnExplicitlyConfiguredCollectionMethod()
    {
        _settings.PaymentCollectionMethod = "automatic";
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(7, "active", "eshop-pro"));

        await CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription => subscription.PaymentCollectionMethod == "automatic"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRejectsAnUnknownPlanWithoutCallingTheBillingSystem()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "does-not-exist")));

        Assert.Equal("does-not-exist", exception.PlanHandle);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeSurfacesValidationFailuresFromTheBillingSystem()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });
        GivenCustomerSubscriptions();
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "POST", "subscriptions.json",
                new[] { "No payment method was on file" }, null));

        var exception = await Assert.ThrowsAsync<BillingRequestInvalidException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro")));

        Assert.Contains("No payment method was on file", exception.Errors);
    }

    [Fact]
    public async Task RejectedCredentialsAreReportedAsAConfigurationProblem()
    {
        _client.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpStatusCode.Unauthorized, "GET", "products.json", Array.Empty<string>(), null));

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().GetPlansAsync());
    }

    [Fact]
    public async Task MissingConfigurationFailsFastWithoutCallingTheBillingSystem()
    {
        _settings.ApiKey = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().GetPlansAsync());
        await _client.DidNotReceiveWithAnyArgs().ListProductsForProductFamilyAsync(default!, default, default, default, default);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsNothingForAShopperWithoutABillingCustomer()
    {
        GivenCustomer(null);

        Assert.Empty(await CreateService().GetSubscriptionsAsync(_subscriber));
        await _client.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    [Fact]
    public async Task GetSubscriptionsReturnsNewestFirst()
    {
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });

        var older = Subscription(1, "canceled", "basic-plan");
        older.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = Subscription(2, "active", "eshop-pro");
        newer.CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        GivenCustomerSubscriptions(older, newer);

        var subscriptions = await CreateService().GetSubscriptionsAsync(_subscriber);

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(subscription => subscription.Id));
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task PlansAreCachedForTheConfiguredDuration()
    {
        _settings.PlanCacheSeconds = 60;
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));

        var service = CreateService();
        await service.GetPlansAsync();
        await service.GetPlansAsync();

        await _client.Received(1).ListProductsForProductFamilyAsync(
            "handle:eshop-subscribe", Arg.Any<int>(), Arg.Any<int>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentSubscribesForTheSameShopperEnrollOnlyOnce()
    {
        GivenCatalog(Product("eshop-pro", "Pro Plan", 29900));
        GivenSite();
        GivenCustomer(new MaxioCustomer { Id = 42, Reference = _subscriber.Reference });

        var created = new List<MaxioSubscription>();
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(_ => created.ToArray());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var subscription = Subscription(100 + created.Count, "active", "eshop-pro");
                created.Add(subscription);
                return subscription;
            });

        var service = CreateService();
        var results = await Task.WhenAll(
            Task.Run(() => service.SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"))),
            Task.Run(() => service.SubscribeAsync(new SubscribeRequest(_subscriber, "eshop-pro"))));

        Assert.Single(created);
        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Equal(results[0].Subscription.Id, results[1].Subscription.Id);
    }
}
