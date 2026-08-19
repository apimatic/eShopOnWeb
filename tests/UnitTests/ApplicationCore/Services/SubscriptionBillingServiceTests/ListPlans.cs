using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class ListPlans
{
    [Fact]
    public async Task ReturnsPlansFromMaxio()
    {
        var maxio = Substitute.For<IMaxioBillingGateway>();
        var logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
        var plans = new[]
        {
            new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false),
            new SubscriptionPlan("basic-plan", "Basic Plan", null, 2900, 1, "month", false)
        };
        maxio.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(plans);

        var result = await new SubscriptionBillingService(maxio, logger).ListPlansAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("eshop-pro", result[0].Handle);
        Assert.Equal(299.00m, result[0].Price);
    }
}
