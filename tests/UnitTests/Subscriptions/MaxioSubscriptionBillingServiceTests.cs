using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "test-family";
    private const string PlanHandle = "test-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionBillingService _service;
    private readonly SubscriberIdentity _subscriber = SubscriberIdentity.FromUser("demouser@microsoft.com");

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });

        _service = new MaxioSubscriptionBillingService(
            _client,
            options,
            new KeyedAsyncLock(),
            Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>());

        _client.ListProductsInFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[] { Product(PlanHandle, "Pro Plan", 29900) });
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingLiveSubscription_WithoutCreatingDuplicate()
    {
        var customer = Customer(1);
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>()).Returns(customer);
        _client.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(555, "active", PlanHandle, customer) });

        var result = await _service.SubscribeAsync(_subscriber, PlanHandle);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IgnoresCanceledSubscription_AndCreatesNew()
    {
        var customer = Customer(1);
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>()).Returns(customer);
        _client.ListCustomerSubscriptionsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(555, "canceled", PlanHandle, customer) });
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(777, "active", PlanHandle, customer));

        var result = await _service.SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(b => b.ProductHandle == PlanHandle && b.CustomerId == 1 && b.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        var created = Customer(9);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>()).Returns(created);
        _client.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(42, "active", PlanHandle, created));

        var result = await _service.SubscribeAsync(_subscriber, PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(42, result.Subscription.Id);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomerBody>(c => c.Reference == _subscriber.Reference && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_RecoversFromCustomerCreationRace()
    {
        // Lookup returns null first (so we try to create), the create loses a race (422), the second lookup finds it.
        var raced = Customer(3);
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, raced);
        _client.CreateCustomerAsync(Arg.Any<CreateCustomerBody>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(
                HttpStatusCode.UnprocessableEntity, "create customer", new[] { "reference: has already been taken" }));
        _client.ListCustomerSubscriptionsAsync(3, Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(99, "active", PlanHandle, raced));

        var result = await _service.SubscribeAsync(_subscriber, PlanHandle);

        Assert.Equal(99, result.Subscription.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscriptionBody>(b => b.CustomerId == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_Throws_ForUnknownPlan()
    {
        await Assert.ThrowsAsync<PlanNotFoundException>(() => _service.SubscribeAsync(_subscriber, "ghost-plan"));
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionBody>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPlans_MapsAndOrdersByPrice()
    {
        _client.ListProductsInFamilyAsync(FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[] { Product("test-pro", "Pro", 29900), Product("test-basic", "Basic", 2900) });

        var plans = await _service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("test-basic", plans[0].Handle); // cheaper first
        Assert.Equal("test-pro", plans[1].Handle);
    }

    private static MaxioProduct Product(string handle, string name, long priceInCents) => new()
    {
        Id = 1,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = FamilyHandle }
    };

    private static MaxioCustomer Customer(long id) => new()
    {
        Id = id,
        Email = "demouser@microsoft.com",
        Reference = "demouser@microsoft.com"
    };

    private static MaxioSubscription Subscription(long id, string state, string handle, MaxioCustomer customer) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = 29900,
        Customer = customer,
        Product = new MaxioProduct { Handle = handle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
    };
}
