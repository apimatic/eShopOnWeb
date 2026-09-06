using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string CustomerReference = "eshoponweb:user:demouser@microsoft.com";
    private const string ProSubscriptionReference = "eshoponweb:sub:demouser@microsoft.com:eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    private static readonly Subscriber Demo = new()
    {
        UserId = "user-1",
        UserName = "demouser@microsoft.com",
        Email = "demouser@microsoft.com"
    };

    private MaxioSubscriptionService CreateService(MaxioOptions? options = null) => new(
        _client,
        new TestOptionsMonitor<MaxioOptions>(options ?? TestOptions.Valid()),
        new MemoryCache(new MemoryCacheOptions()),
        new KeyedAsyncLock(),
        NullLogger<MaxioSubscriptionService>.Instance);

    private void GivenPlans(params string[] handles) =>
        _client.ListProductsForProductFamilyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(handles.Select((handle, index) => new MaxioProduct
            {
                Id = index + 1,
                Handle = handle,
                Name = handle,
                PriceInCents = (index + 1) * 1000,
                Interval = 1,
                IntervalUnit = "month"
            }).ToList());

    private static MaxioSubscription Subscription(int id, string state, string planHandle, string? reference = null) => new()
    {
        Id = id,
        State = state,
        Reference = reference,
        Product = new MaxioProduct { Handle = planHandle, Name = planHandle, Interval = 1, IntervalUnit = "month" },
        Customer = new MaxioCustomer { Id = 500, Reference = CustomerReference }
    };

    private static MaxioApiException DuplicateReference() => new(
        HttpStatusCode.UnprocessableEntity,
        "POST",
        "/subscriptions.json",
        new[] { "Reference: must be unique - that value has been taken." });

    [Fact]
    public async Task PlansAreListedFromTheConfiguredProductFamilyByHandle()
    {
        GivenPlans("eshop-pro", "basic-plan");

        var plans = await CreateService().ListPlansAsync();

        await _client.Received(1).ListProductsForProductFamilyAsync("handle:eshop-subscribe", false, Arg.Any<CancellationToken>());
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, plans.Select(plan => plan.Handle));
    }

    [Fact]
    public async Task SubscribingCreatesTheCustomerAndTheSubscription()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", ProSubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        Assert.True(result.Subscription.IsLive);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(customer =>
                customer.Reference == CustomerReference &&
                customer.Email == "demouser@microsoft.com" &&
                !string.IsNullOrWhiteSpace(customer.FirstName) &&
                !string.IsNullOrWhiteSpace(customer.LastName)),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(subscription =>
                subscription.ProductHandle == "eshop-pro" &&
                subscription.CustomerId == 500 &&
                subscription.Reference == ProSubscriptionReference &&
                subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExistingCustomerIsReused()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", ProSubscriptionReference));

        await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACustomerCreatedByAConcurrentRequestIsAdopted()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>())
            .Throws(DuplicateReference());
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", ProSubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.Equal(900, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheSameSubscription()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", ProSubscriptionReference));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALiveSubscriptionOnTheSamePlanUnderAnotherReferenceAlsoCountsAsSubscribed()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(901, "trialing", "eshop-pro", "created-in-the-maxio-ui") });

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(901, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LosingTheRaceToCreateASubscriptionReturnsTheWinner()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(_ => null, _ => Subscription(900, "active", "eshop-pro", ProSubscriptionReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(DuplicateReference());

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
    }

    [Fact]
    public async Task AnEndedSubscriptionCanBeStartedAgainUnderAFreshReference()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "canceled", "eshop-pro", ProSubscriptionReference));
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(901, "active", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(901, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(subscription =>
                subscription.Reference!.StartsWith(ProSubscriptionReference + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEndedSubscriptionDoesNotHideALiveOneOnTheSamePlan()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "canceled", "eshop-pro", ProSubscriptionReference));
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(900, "canceled", "eshop-pro", ProSubscriptionReference),
                Subscription(901, "active", "eshop-pro", "created-in-the-maxio-ui")
            });

        var result = await CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(901, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReplayedIdempotencyKeyReturnsTheOriginalSubscription()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync("eshoponweb:sub:demouser@microsoft.com:key:checkout-42", Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "canceled", "eshop-pro"));

        var result = await CreateService().SubscribeAsync(new SubscribeRequest
        {
            Subscriber = Demo,
            PlanHandle = "eshop-pro",
            IdempotencyKey = "checkout-42"
        });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(900, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownPlanIsRejected()
    {
        GivenPlans("eshop-pro");

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "not-a-plan" }));

        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheConfiguredDefaultPlanIsUsedWhenTheRequestNamesNone()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "active", "eshop-pro", ProSubscriptionReference));

        var service = CreateService(TestOptions.Valid(options => options.DefaultPlanHandle = "eshop-pro"));
        var result = await service.SubscribeAsync(new SubscribeRequest { Subscriber = Demo });

        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task WithoutAPlanOrADefaultTheRequestIsRejected()
    {
        GivenPlans("eshop-pro");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo }));
    }

    [Fact]
    public async Task AShopperWhoNeverSubscribedHasNoSubscriptions()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().ListSubscriptionsAsync(Demo);

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriptionsAreListedForTheResolvedCustomer()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(900, "active", "eshop-pro", ProSubscriptionReference) });

        var subscriptions = await CreateService().ListSubscriptionsAsync(Demo);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(900, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task ProviderRejectionsAreTranslatedForTheApiSurface()
    {
        GivenPlans("eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 500, Reference = CustomerReference });
        _client.FindSubscriptionAsync(ProSubscriptionReference, Arg.Any<CancellationToken>()).Returns((MaxioSubscription?)null);
        _client.ListCustomerSubscriptionsAsync(500, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(
                HttpStatusCode.UnprocessableEntity,
                "POST",
                "/subscriptions.json",
                new[] { "No payment method was on file for the $299.00 balance" }));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            CreateService().SubscribeAsync(new SubscribeRequest { Subscriber = Demo, PlanHandle = "eshop-pro" }));

        Assert.True(exception.IsRequestRejected);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.ProviderErrors);
    }

    [Fact]
    public async Task MissingConfigurationIsReportedAsAConfigurationFailure()
    {
        var service = CreateService(new MaxioOptions());

        await Assert.ThrowsAsync<BillingConfigurationException>(() => service.ListPlansAsync());
    }
}
