using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public void BuildSubscriptionReferenceIsStableForUserAndPlan()
    {
        var reference = MaxioSubscriptionBillingService.BuildSubscriptionReference("user-1", "eshop-pro");
        Assert.Equal("user-1:eshop-pro", reference);
    }

    [Fact]
    public void SplitNameUsesEmailLocalPartWhenDisplayNameIsEmail()
    {
        var shopper = new ShopperIdentity("id", "demouser@microsoft.com", "demouser@microsoft.com");
        var (first, last) = MaxioSubscriptionBillingService.SplitName(shopper);
        Assert.Equal("demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }
}
