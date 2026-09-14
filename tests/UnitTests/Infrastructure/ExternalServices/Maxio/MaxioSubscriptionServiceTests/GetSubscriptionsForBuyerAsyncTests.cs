using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio.Wire;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.ExternalServices.Maxio.MaxioSubscriptionServiceTests;

public class GetSubscriptionsForBuyerAsyncTests
{
    private const string BuyerEmail = "buyer@example.com";

    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionService _sut;

    public GetSubscriptionsForBuyerAsyncTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        _sut = new MaxioSubscriptionService(_client, options);
    }

    [Fact]
    public async Task ReturnsEmptyList_WhenBuyerHasNeverSubscribed()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await _sut.GetSubscriptionsForBuyerAsync(BuyerEmail);

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheBuyersSubscriptions_NewestFirst()
    {
        _client.FindCustomerByReferenceAsync(BuyerEmail, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = BuyerEmail });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new() { Id = 1, State = "active", CreatedAt = System.DateTimeOffset.Parse("2026-01-01T00:00:00Z"), Product = new MaxioProduct { Handle = "basic-plan" } },
                new() { Id = 2, State = "active", CreatedAt = System.DateTimeOffset.Parse("2026-06-01T00:00:00Z"), Product = new MaxioProduct { Handle = "eshop-pro" } }
            });

        var result = await _sut.GetSubscriptionsForBuyerAsync(BuyerEmail);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].MaxioSubscriptionId);
        Assert.Equal(1, result[1].MaxioSubscriptionId);
    }
}
