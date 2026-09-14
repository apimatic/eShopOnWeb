using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class GetSubscriptionsForBuyerAsync
{
    private const string BuyerId = "demouser@microsoft.com";
    private const string CustomerReference = "eshoponweb:demouser@microsoft.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();

    [Fact]
    public async Task ReturnsEmpty_WhenBuyerHasNoMaxioCustomerYet()
    {
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = new MaxioSubscriptionService(_client);

        var result = await service.GetSubscriptionsForBuyerAsync(BuyerId);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsBuyerSubscriptions_WhenCustomerExists()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = CustomerReference };
        _client.FindCustomerByReferenceAsync(CustomerReference, Arg.Any<CancellationToken>())
            .Returns(customer);

        var subscription = new MaxioSubscription
        {
            Id = 42,
            State = "active",
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" },
            Customer = customer
        };
        _client.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new[] { subscription });

        var service = new MaxioSubscriptionService(_client);

        var result = await service.GetSubscriptionsForBuyerAsync(BuyerId);

        var enrollment = Assert.Single(result);
        Assert.Equal(42, enrollment.SubscriptionId);
        Assert.Equal("eshop-pro", enrollment.PlanHandle);
        Assert.Equal(299m, enrollment.Price);
    }
}
