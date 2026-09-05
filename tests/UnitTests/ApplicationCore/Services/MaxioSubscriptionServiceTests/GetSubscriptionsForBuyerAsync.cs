using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioSubscriptionServiceTests;

public class GetSubscriptionsForBuyerAsync
{
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };
    private readonly IMaxioClient _client = Substitute.For<IMaxioClient>();

    [Fact]
    public async Task ReturnsEmptyListWithoutCallingListSubscriptionsWhenBuyerHasNoMaxioCustomer()
    {
        _client.FindCustomerByReferenceAsync("never-subscribed@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = new MaxioSubscriptionService(_client, _options);
        var subscriptions = await service.GetSubscriptionsForBuyerAsync("never-subscribed@example.com");

        Assert.Empty(subscriptions);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
