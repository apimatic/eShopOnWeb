using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingServiceTests;

public class GetSubscriptionsForBuyerAsync
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioBillingService _sut;

    public GetSubscriptionsForBuyerAsync()
    {
        _sut = new MaxioBillingService(_client, new MaxioBuyerLock(), Options.Create(new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe"
        }));
    }

    [Fact]
    public async Task ReturnsEmptyListWhenTheBuyerHasNoMaxioCustomerYet()
    {
        _client.FindCustomerByReferenceAsync("nobody@example.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer)null!);

        var result = await _sut.GetSubscriptionsForBuyerAsync("nobody@example.com");

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
