using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserName = "demouser@microsoft.com";
    private const string CustomerReference = "eshoponweb:demouser@microsoft.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static readonly SubscriberIdentity Subscriber = new()
    {
        UserName = UserName,
        Email = UserName
    };

    private static MaxioProduct ProPlan() => new()
    {
        Id = 7126957,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
    };

    private static MaxioProduct BasicPlan() => new()
    {
        Id = 7126958,
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceInCents = 2900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
    };

    private static MaxioCustomer Customer() => new()
    {
        Id = 4242,
        Email = UserName,
        Reference = CustomerReference
    };

    private static MaxioSubscription Subscription(string state, MaxioProduct product, long id = 99,
        string? reference = null) => new()
        {
            Id = id,
            State = state,
            Product = product,
            Customer = Customer(),
            ProductPriceInCents = product.PriceInCents,
            Reference = reference,
            CreatedAt = DateTimeOffset.UtcNow,
            CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            Currency = "USD"
        };

    private MaxioSubscriptionService CreateService()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSite
            {
                Currency = "USD",
                Subdomain = "acme",
                RelationshipInvoicingEnabled = true
            });

        return new MaxioSubscriptionService(_client, new StaticOptionsMonitor<MaxioOptions>(_options),
            new MemoryCache(new MemoryCacheOptions()), new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private void PublishPlans(params MaxioProduct[] products) =>
        _client.ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, Arg.Any<int>(),
            Arg.Any<CancellationToken>()).Returns(products.ToList());

    [Fact]
    public async Task ListPlansPublishesTheProductsOfTheConfiguredFamilyCheapestFirst()
    {
        PublishPlans(ProPlan(), BasicPlan());

        var plans = (await CreateService().ListPlansAsync()).ToList();

        Assert.Collection(plans,
            plan =>
            {
                Assert.Equal("basic-plan", plan.Handle);
                Assert.Equal(29.00m, plan.Price);
                Assert.Equal("USD", plan.Currency);
                Assert.Equal("month", plan.IntervalUnit);
            },
            plan =>
            {
                Assert.Equal("eshop-pro", plan.Handle);
                Assert.Equal(299.00m, plan.Price);
            });
    }

    [Fact]
    public async Task ListPlansLeavesOutArchivedProducts()
    {
        var archived = BasicPlan();
        archived.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);
        PublishPlans(ProPlan(), archived);

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ListPlansStillWorksWhenTheSiteCurrencyCannotBeRead()
    {
        PublishPlans(ProPlan());
        var service = CreateService();
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException("site unavailable", 403));

        var plan = Assert.Single(await service.ListPlansAsync());

        Assert.Null(plan.Currency);
        Assert.Equal(29900, plan.PriceInCents);
    }

    [Fact]
    public async Task SubscribeCreatesTheCustomerAndTheSubscription()
    {
        PublishPlans(ProPlan(), BasicPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan()));

        var result = await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(SubscriptionState.Active, result.Subscription.State);
        Assert.Equal("active", result.Subscription.StateName);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerRequest>(request =>
                request.Customer.Reference == CustomerReference &&
                request.Customer.Email == UserName &&
                request.Customer.FirstName.Length > 0 &&
                request.Customer.LastName.Length > 0),
            Arg.Any<CancellationToken>());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(request =>
                request.Subscription.ProductHandle == "eshop-pro" &&
                request.Subscription.CustomerId == 4242 &&
                request.Subscription.PaymentCollectionMethod == "remittance" &&
                request.Subscription.Reference == CustomerReference + ":eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesAnExistingCustomer()
    {
        PublishPlans(ProPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan()));

        await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeReusesTheCustomerCreatedByAConcurrentRequest()
    {
        PublishPlans(ProPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer());
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioValidationException("reference: must be unique", 422,
                new[] { "reference: must be unique" }));
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan()));

        var result = await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(4242, result.Subscription.CustomerId);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeDoesNotEnrollTwiceInAPlanTheShopperAlreadyHolds()
    {
        PublishPlans(ProPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription("active", ProPlan(), id: 555) });

        var result = await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeIgnoresASubscriptionToADifferentPlan()
    {
        PublishPlans(ProPlan(), BasicPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription("active", BasicPlan(), id: 555) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan(), id: 556));

        var result = await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(556, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeStartsAFreshSubscriptionAfterTheEarlierOneEnded()
    {
        PublishPlans(ProPlan());
        var canceled = Subscription("canceled", ProPlan(), id: 555,
            reference: CustomerReference + ":eshop-pro");
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { canceled });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan(), id: 556));

        var result = await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);

        // The reference of the ended subscription is taken, so the new one gets a distinct suffix.
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(request =>
                request.Subscription.Reference != null &&
                request.Subscription.Reference.StartsWith(CustomerReference + ":eshop-pro:")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeUsesTheOnlyPublishedPlanWhenNoHandleIsGiven()
    {
        PublishPlans(ProPlan());
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan()));

        var result = await CreateService().SubscribeAsync(Subscriber, planHandle: null);

        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeRejectsAPlanThatIsNotPublished()
    {
        PublishPlans(ProPlan(), BasicPlan());

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(Subscriber, "enterprise"));

        Assert.Contains("eshop-pro", exception.Message);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeRequiresAPlanHandleWhenSeveralArePublished()
    {
        PublishPlans(ProPlan(), BasicPlan());

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => CreateService().SubscribeAsync(Subscriber, planHandle: null));

        Assert.Contains("basic-plan", exception.Message);
    }

    [Fact]
    public async Task ListSubscriptionsIsEmptyForAShopperWhoNeverSubscribed()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        Assert.Empty(await CreateService().ListSubscriptionsAsync(Subscriber));

        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsReportsWhatTheShopperHolds()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription("active", ProPlan(), id: 555) });

        var subscription = Assert.Single(await CreateService().ListSubscriptionsAsync(Subscriber));

        Assert.Equal(555, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.State.IsLive());
        Assert.Equal(4242, subscription.CustomerId);
    }

    [Fact]
    public async Task ReportsUnknownStatesWithoutFailing()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription("a_state_from_the_future", ProPlan()) });

        var subscription = Assert.Single(await CreateService().ListSubscriptionsAsync(Subscriber));

        Assert.Equal(SubscriptionState.Unknown, subscription.State);
        Assert.Equal("a_state_from_the_future", subscription.StateName);
        Assert.False(subscription.State.IsLive());
    }

    [Fact]
    public async Task SubscribeInvoicesTheShopperBecauseNoCardIsCaptured()
    {
        PublishPlans(ProPlan());
        StubAnEmptyCustomer();

        await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        // The plan does not require a payment method, and the site uses Relationship Invoicing.
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(request =>
                request.Subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeUsesTheLegacyCollectionMethodOnAStatementsSite()
    {
        PublishPlans(ProPlan());
        StubAnEmptyCustomer();
        var service = CreateService();
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Returns(new MaxioSite { Currency = "USD", RelationshipInvoicingEnabled = false });

        await service.SubscribeAsync(Subscriber, "eshop-pro");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(request =>
                request.Subscription.PaymentCollectionMethod == "invoice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeLeavesAPlanThatDemandsAPaymentMethodOnAutomaticCollection()
    {
        var cardRequired = ProPlan();
        cardRequired.RequireCreditCard = true;
        PublishPlans(cardRequired);
        StubAnEmptyCustomer();

        await CreateService().SubscribeAsync(Subscriber, "eshop-pro");

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(request =>
                request.Subscription.PaymentCollectionMethod == "automatic"),
            Arg.Any<CancellationToken>());
    }

    private void StubAnEmptyCustomer()
    {
        _client.ReadCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
        _client.ListCustomerSubscriptionsAsync(4242, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription("active", ProPlan()));
    }

    [Fact]
    public async Task RefusesToWorkWithoutConfiguration()
    {
        _options.ApiKey = null;
        _options.ProductFamilyHandle = null;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateService().ListPlansAsync());
    }
}
