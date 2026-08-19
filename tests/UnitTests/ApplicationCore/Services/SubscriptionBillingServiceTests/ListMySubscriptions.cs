using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class ListMySubscriptions
{
    [Fact]
    public async Task ReturnsEmptyListWhenCustomerDoesNotExist()
    {
        var gateway = Substitute.For<IAdvancedBillingGateway>();
        var settings = Substitute.For<IBillingCatalogSettings>();
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        gateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var service = new SubscriptionBillingService(gateway, settings, logger);
        var result = await service.ListMySubscriptionsAsync(new ShopperIdentity
        {
            UserId = "user-1",
            Email = "demouser@microsoft.com",
            UserName = "demouser@microsoft.com"
        });

        Assert.Empty(result);
        await gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsCustomerSubscriptionsFromGateway()
    {
        var gateway = Substitute.For<IAdvancedBillingGateway>();
        var settings = Substitute.For<IBillingCatalogSettings>();
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        gateway.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 5, Reference = "user-1" });
        gateway.ListCustomerSubscriptionsAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>
            {
                new() { Id = 1, State = "active", ProductHandle = "eshop-pro", ProductName = "Pro Plan", PriceInCents = 29900 }
            });

        var service = new SubscriptionBillingService(gateway, settings, logger);
        var result = await service.ListMySubscriptionsAsync(new ShopperIdentity
        {
            UserId = "user-1",
            Email = "demouser@microsoft.com",
            UserName = "demouser@microsoft.com"
        });

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].ProductHandle);
    }
}
