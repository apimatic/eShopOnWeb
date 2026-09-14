using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private const string BuyerId = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";
    private const string CustomerReference = "eshoponweb:demouser@microsoft.com";
    private const string SubscriptionReference = "eshoponweb:demouser@microsoft.com:eshop-pro";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    [Fact]
    public async Task CreatesCustomerThenSubscription_WhenNeitherExists()
    {
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        var newCustomer = new MaxioCustomer { Id = 1, Reference = CustomerReference };
        _client.CreateCustomerAsync(CustomerReference, BuyerId, "Demouser", "Customer", Arg.Any<CancellationToken>())
            .Returns(newCustomer);

        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        var created = MakeSubscription(id: 42);
        _client.CreateSubscriptionWithoutPaymentMethodAsync(CustomerReference, PlanHandle, SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(created);

        var service = new MaxioSubscriptionService(_client);

        var enrollment = await service.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(42, enrollment.SubscriptionId);
        Assert.False(enrollment.AlreadyExisted);
        await _client.Received(1).CreateCustomerAsync(CustomerReference, BuyerId, "Demouser", "Customer", Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionWithoutPaymentMethodAsync(CustomerReference, PlanHandle, SubscriptionReference, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingSubscription_WithoutCreatingCustomerOrSubscription_WhenAlreadySubscribed()
    {
        var existingCustomer = new MaxioCustomer { Id = 1, Reference = CustomerReference };
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(existingCustomer);

        var existingSubscription = MakeSubscription(id: 42);
        _client.FindSubscriptionByReferenceAsync(SubscriptionReference, Arg.Any<CancellationToken>())
            .Returns(existingSubscription);

        var service = new MaxioSubscriptionService(_client);

        var enrollment = await service.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(42, enrollment.SubscriptionId);
        Assert.True(enrollment.AlreadyExisted);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionWithoutPaymentMethodAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotSubscribeTwoDifferentBuyersToTheSameCustomerOrSubscriptionReference()
    {
        _client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MaxioCustomer { Id = 1, Reference = ci.ArgAt<string>(0) });
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionWithoutPaymentMethodAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeSubscription(id: 1));

        var service = new MaxioSubscriptionService(_client);

        await service.SubscribeAsync("alice@microsoft.com", "alice@microsoft.com", PlanHandle);
        await service.SubscribeAsync("bob@microsoft.com", "bob@microsoft.com", PlanHandle);

        await _client.Received(1).FindCustomerByReferenceAsync("eshoponweb:alice@microsoft.com", Arg.Any<CancellationToken>());
        await _client.Received(1).FindCustomerByReferenceAsync("eshoponweb:bob@microsoft.com", Arg.Any<CancellationToken>());
        await _client.Received(1).FindSubscriptionByReferenceAsync("eshoponweb:alice@microsoft.com:eshop-pro", Arg.Any<CancellationToken>());
        await _client.Received(1).FindSubscriptionByReferenceAsync("eshoponweb:bob@microsoft.com:eshop-pro", Arg.Any<CancellationToken>());
    }

    private static MaxioSubscription MakeSubscription(long id) => new()
    {
        Id = id,
        State = "active",
        ProductPriceInCents = 29900,
        Product = new MaxioProduct { Handle = PlanHandle, Name = "Pro Plan" },
        Customer = new MaxioCustomer { Id = 1, Reference = CustomerReference }
    };
}
