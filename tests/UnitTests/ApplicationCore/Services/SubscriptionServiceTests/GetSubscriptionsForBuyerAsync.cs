using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptionsForBuyerAsync
{
    private const string BuyerReference = "buyer@example.com";

    private readonly IMaxioService _mockMaxioService = Substitute.For<IMaxioService>();

    [Fact]
    public async Task ReturnsEmptyListWhenBuyerHasNoMaxioCustomerYet()
    {
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.GetSubscriptionsForBuyerAsync(BuyerReference);

        Assert.Empty(result);
        await _mockMaxioService.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsCustomerSubscriptionsWhenBuyerExists()
    {
        var customer = new MaxioCustomer { Id = 11, Reference = BuyerReference };
        _mockMaxioService.FindCustomerByReferenceAsync(BuyerReference, Arg.Any<CancellationToken>())
            .Returns(customer);
        var subscriptions = new[] { new MaxioSubscription { Id = 1 }, new MaxioSubscription { Id = 2 } };
        _mockMaxioService.ListCustomerSubscriptionsAsync(11, Arg.Any<CancellationToken>())
            .Returns(subscriptions);

        var subscriptionService = new SubscriptionService(_mockMaxioService);
        var result = await subscriptionService.GetSubscriptionsForBuyerAsync(BuyerReference);

        Assert.Equal(subscriptions, result);
    }
}
