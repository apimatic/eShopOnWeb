using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionModelTests
{
    [Theory]
    [InlineData(29900, "$299.00")]
    [InlineData(2900, "$29.00")]
    [InlineData(0, "$0.00")]
    [InlineData(1, "$0.01")]
    public void SubscriptionPlan_FormatsPriceFromCents(long cents, string expected)
    {
        var plan = new SubscriptionPlan { PriceInCents = cents };
        Assert.Equal(expected, plan.PriceFormatted);
    }

    [Fact]
    public void CustomerSubscription_FormatsPriceFromCents()
    {
        var subscription = new CustomerSubscription { PriceInCents = 29900 };
        Assert.Equal("$299.00", subscription.PriceFormatted);
    }
}
