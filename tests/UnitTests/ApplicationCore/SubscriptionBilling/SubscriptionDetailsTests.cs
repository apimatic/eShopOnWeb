using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.SubscriptionBilling;

public class SubscriptionDetailsTests
{
    [Fact]
    public void PriceConvertsCentsToDollars()
    {
        var subscription = new SubscriptionDetails { ProductPriceInCents = 29900 };

        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal(299m, subscription.Price);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("past_due", true)]
    public void IsCurrentReflectsState(string state, bool expected)
    {
        var subscription = new SubscriptionDetails { State = state };

        Assert.Equal(expected, subscription.IsCurrent);
    }
}
