using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class GetSubscriptionsForBuyerAsync
{
    private const string BuyerId = "buyer@example.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionService _sut;

    public GetSubscriptionsForBuyerAsync()
    {
        _sut = new MaxioSubscriptionService(_client, Options.Create(new MaxioOptions
        {
            ApiKey = "unused",
            Subdomain = "unused",
            ProductFamilyHandle = "eshop-subscribe"
        }));
    }

    [Fact]
    public async Task ReturnsEmptyList_WhenBuyerHasNoMaxioCustomerYet()
    {
        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>()).Returns((MaxioCustomerModel?)null);

        var result = await _sut.GetSubscriptionsForBuyerAsync(BuyerId);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsMappedSubscriptions_WhenCustomerExists()
    {
        _client.FindCustomerByReferenceAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerModel { Id = 5, Reference = BuyerId });
        _client.ListCustomerSubscriptionsAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionModel>
            {
                new()
                {
                    Id = 10,
                    State = "active",
                    Product = new MaxioProductModel { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
                }
            });

        var result = await _sut.GetSubscriptionsForBuyerAsync(BuyerId);

        var subscription = Assert.Single(result);
        Assert.Equal(10, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
    }
}
