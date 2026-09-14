using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionPriceFormattingTests
{
    [Theory]
    [InlineData(29900, "$299.00")]
    [InlineData(2900, "$29.00")]
    [InlineData(0, "$0.00")]
    [InlineData(1, "$0.01")]
    public void Plan_FormattedPrice_FormatsCentsAsUsd(int cents, string expected)
    {
        var plan = new SubscriptionPlan { PriceInCents = cents };

        Assert.Equal(expected, plan.FormattedPrice);
    }

    [Fact]
    public void Subscription_FormattedPrice_FormatsCentsAsUsd()
    {
        var subscription = new Subscription { PriceInCents = 29900 };

        Assert.Equal("$299.00", subscription.FormattedPrice);
    }
}
