using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptionsForUserAsync
{
    private const string UserName = "buyer@example.com";

    private readonly IMaxioClient _mockMaxioClient = Substitute.For<IMaxioClient>();
    private readonly MaxioOptions _maxioOptions = new() { ProductFamilyHandle = "eshop-subscribe" };

    [Fact]
    public async Task WhenUserHasNoMaxioCustomerYet_ReturnsEmpty_WithoutListingSubscriptions()
    {
        _mockMaxioClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var sut = new SubscriptionService(_mockMaxioClient, _maxioOptions);

        var result = await sut.GetSubscriptionsForUserAsync(UserName);

        Assert.Empty(result);
        await _mockMaxioClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenUserHasACustomer_ReturnsTheirSubscriptions()
    {
        var customer = new MaxioCustomer { Id = 7, Reference = UserName };
        _mockMaxioClient.FindCustomerByReferenceAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(customer);
        var subscriptions = new List<MaxioSubscription>
        {
            new() { Id = 1, CustomerId = customer.Id, ProductHandle = "eshop-pro", State = "active" }
        };
        _mockMaxioClient.ListCustomerSubscriptionsAsync(customer.Id, Arg.Any<CancellationToken>())
            .Returns(subscriptions);

        var sut = new SubscriptionService(_mockMaxioClient, _maxioOptions);

        var result = await sut.GetSubscriptionsForUserAsync(UserName);

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].ProductHandle);
    }
}
