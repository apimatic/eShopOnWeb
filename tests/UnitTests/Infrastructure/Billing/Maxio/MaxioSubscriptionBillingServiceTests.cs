using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Family = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionBillingService _service;
    private readonly SubscriberIdentity _subscriber = new("demouser@microsoft.com", "demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = Family
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        _service = new MaxioSubscriptionBillingService(_client, options, logger);
    }

    [Fact]
    public async Task GetAvailablePlans_FiltersByFamily_ExcludesArchivedAndHandleless_OrdersByPrice()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Product("eshop-pro", "Pro Plan", 29900, Family),
            Product("basic-plan", "Basic Plan", 2900, Family),
            Product("other-fam", "Other", 100, "different-family"),
            Product(handle: null, "No Handle", 50, Family),
            Archived("archived-plan", "Archived", 500, Family)
        });

        var plans = await _service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle); // ordered by price ascending
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299m, plans[1].Price);
    }

    [Fact]
    public async Task Subscribe_Throws_WhenPlanHandleUnknown()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Product(ProPlan, "Pro Plan", 29900, Family)
        });

        await Assert.ThrowsAsync<PlanNotFoundException>(() => _service.SubscribeAsync(_subscriber, "nope"));
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>()).Returns(Customer(42));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(777, "active", ProPlan, 29900));

        var result = await _service.SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(r => r.Subscription.CustomerId == 42 && r.Subscription.ProductHandle == ProPlan),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_DoesNotCreateCustomer_WhenCustomerAlreadyExists()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer(9));
        _client.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(1, "active", ProPlan, 29900));

        await _service.SubscribeAsync(_subscriber, ProPlan);

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WhenActiveSubscriptionToSamePlanExists()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer(9));
        _client.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            Subscription(555, "active", ProPlan, 29900)
        });

        var result = await _service.SubscribeAsync(_subscriber, ProPlan);

        Assert.False(result.Created);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesNew_WhenOnlyCanceledSubscriptionToPlanExists()
    {
        ArrangePlans();
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer(9));
        _client.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            Subscription(100, "canceled", ProPlan, 29900)
        });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(200, "active", ProPlan, 29900));

        var result = await _service.SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.Created);
        Assert.Equal(200, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_ResolvesExistingCustomer_WhenCreateLosesReferenceRace()
    {
        ArrangePlans();
        // First lookup: no customer. Second lookup (after 422): customer now exists.
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, Customer(77));
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(422, new[] { "Reference: already been taken" }, "{}"));
        _client.ListCustomerSubscriptionsAsync(77, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(300, "active", ProPlan, 29900));

        var result = await _service.SubscribeAsync(_subscriber, ProPlan);

        Assert.True(result.Created);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionRequest>(r => r.Subscription.CustomerId == 77),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subs = await _service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(subs);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrapsMaxioApiException_AsBillingUpstreamException()
    {
        _client.ListProductsAsync(Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(500, new[] { "boom" }, "{}"));

        var ex = await Assert.ThrowsAsync<BillingUpstreamException>(() => _service.GetAvailablePlansAsync());
        Assert.Equal(500, ex.StatusCode);
    }

    private void ArrangePlans() =>
        _client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            Product(ProPlan, "Pro Plan", 29900, Family)
        });

    private static MaxioProduct Product(string? handle, string name, long priceInCents, string familyHandle) => new()
    {
        Id = 1,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = familyHandle }
    };

    private static MaxioProduct Archived(string handle, string name, long priceInCents, string familyHandle)
    {
        var product = Product(handle, name, priceInCents, familyHandle);
        product.ArchivedAt = System.DateTimeOffset.UtcNow;
        return product;
    }

    private static MaxioCustomer Customer(long id) => new() { Id = id, Reference = "eshoponweb:demouser@microsoft.com", Email = "demouser@microsoft.com" };

    private static MaxioSubscription Subscription(long id, string state, string planHandle, long priceInCents) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = priceInCents,
        Product = new MaxioProduct { Handle = planHandle, Name = planHandle, PriceInCents = priceInCents }
    };
}
