using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class ShopperNameAndReference
{
    [Fact]
    public void SplitShopperNameUsesEmailLocalPart()
    {
        var shopper = new Shopper("id", "demouser@microsoft.com", "demouser@microsoft.com");
        var (first, last) = SubscriptionBillingService.SplitShopperName(shopper);

        Assert.Equal("demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public void BuildSubscriptionReferenceCombinesUserAndPlan()
    {
        Assert.Equal("user-1:eshop-pro", SubscriptionBillingService.BuildSubscriptionReference("user-1", "eshop-pro"));
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("trial_ended", false)]
    public void IsLiveClassifiesStates(string state, bool expected)
    {
        Assert.Equal(expected, SubscriptionBillingService.IsLive(state));
    }
}
