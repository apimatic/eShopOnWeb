using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptionsAsync : SubscriptionServiceFixture
{
    [Fact]
    public async Task ReturnsNothingForAShopperWhoHasNeverSubscribed()
    {
        Gateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var service = CreateService();

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        await Gateway.DidNotReceive().ListSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LooksTheShopperUpByTheirDerivedBillingReference()
    {
        GivenCustomerExists();
        GivenSubscriptions(Subscription(id: 5));

        var service = CreateService();

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Single(subscriptions);
        await Gateway.Received(1).FindCustomerByReferenceAsync(
            BillingReferences.ForUser(UserName),
            Arg.Any<CancellationToken>());
    }
}
