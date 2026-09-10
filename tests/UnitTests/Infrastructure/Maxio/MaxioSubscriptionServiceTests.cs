using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const int FamilyId = 42;
    private const string FamilyHandle = "eshop-subscribe";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly IAppLogger<MaxioSubscriptionService> _logger = Substitute.For<IAppLogger<MaxioSubscriptionService>>();
    private readonly SubscriberIdentity _subscriber = new("user@example.com", "user@example.com", "User", "Example");

    private MaxioSubscriptionService CreateService()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "sub",
            ProductFamilyHandle = FamilyHandle
        });

        _client.GetProductFamiliesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductFamily> { new() { Id = FamilyId, Handle = FamilyHandle, Name = "eShop" } });

        _client.GetProductsByFamilyAsync(FamilyId, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product("eshop-pro", "Pro Plan", 29900),
                Product("basic-plan", "Basic Plan", 2900)
            });

        return new MaxioSubscriptionService(_client, options, _logger);
    }

    private static MaxioProduct Product(string handle, string name, long priceCents) => new()
    {
        Id = handle.GetHashCode() & 0x7fffffff,
        Handle = handle,
        Name = name,
        PriceInCents = priceCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false
    };

    private static MaxioCustomer Customer(long id, string reference) => new()
    {
        Id = id,
        Reference = reference,
        Email = reference
    };

    private static MaxioSubscription Subscription(long id, string handle, string state, long customerId) => new()
    {
        Id = id,
        State = state,
        Product = new MaxioProduct { Handle = handle, Name = handle, Interval = 1, IntervalUnit = "month", PriceInCents = 2900 },
        Customer = new MaxioCustomer { Id = customerId },
        Currency = "USD"
    };

    [Fact]
    public async Task GetPlansAsync_MapsProductsFromConfiguredFamily()
    {
        var service = CreateService();

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Price == 299m && p.IntervalUnit == "month");
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Price == 29m);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsUnknownPlanException()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<UnknownPlanException>(
            () => service.SubscribeAsync(_subscriber, "no-such-plan"));

        Assert.Contains("basic-plan", ex.AvailableHandles);
        Assert.Contains("eshop-pro", ex.AvailableHandles);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_NoExistingCustomer_CreatesCustomerThenSubscription()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<CustomerInput>(), Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(500, "eshop-pro", "active", 100));

        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(500, result.Id);
        Assert.Equal("active", result.State);
        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<CustomerInput>(c => c.Reference == _subscriber.Reference && c.Email == _subscriber.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<SubscriptionInput>(s => s.ProductHandle == "eshop-pro" && s.CustomerId == 100 && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ExistingCustomer_DoesNotCreateCustomer()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(501, "eshop-pro", "active", 100));

        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<CustomerInput>(), Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ActiveSubscriptionToSamePlan_ReturnsExistingWithoutCreating()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(777, "eshop-pro", "active", 100) });

        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(777, result.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_OnlyCanceledSubscription_CreatesNewOne()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { Subscription(777, "eshop-pro", "canceled", 100) });
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(888, "eshop-pro", "active", 100));

        var result = await service.SubscribeAsync(_subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(888, result.Id);
        await _client.Received(1).CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_NullPlanHandle_FallsBackToDefaultPlan()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(900, "eshop-pro", "active", 100));

        var result = await service.SubscribeAsync(_subscriber, planHandle: null);

        Assert.Equal("eshop-pro", result.PlanHandle);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<SubscriptionInput>(s => s.ProductHandle == "eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_NoCustomer_ReturnsEmpty()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Empty(result);
        await _client.DidNotReceive().GetCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ExistingCustomer_ReturnsMappedSubscriptions()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(1, "eshop-pro", "active", 100),
                Subscription(2, "basic-plan", "canceled", 100)
            });

        var result = await service.GetSubscriptionsAsync(_subscriber);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Id == 1 && s.State == "active");
        Assert.Contains(result, s => s.Id == 2 && s.State == "canceled");
    }

    [Fact]
    public async Task SubscribeAsync_ClientThrowsMaxioApiException_SurfacesBillingException()
    {
        var service = CreateService();
        _client.LookupCustomerByReferenceAsync(_subscriber.Reference, Arg.Any<CancellationToken>())
            .Returns(Customer(100, _subscriber.Reference));
        _client.GetCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<SubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscription>(_ => throw new MaxioApiException(
                System.Net.HttpStatusCode.UnprocessableEntity, new[] { "boom" }, "POST subscriptions.json"));

        await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(_subscriber, "eshop-pro"));
    }
}
