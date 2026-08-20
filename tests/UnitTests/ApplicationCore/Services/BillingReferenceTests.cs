using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class BillingReferenceTests
{
    [Fact]
    public void ForCustomer_PrefixesShopperIdentity()
    {
        Assert.Equal("eshop:demouser@microsoft.com", BillingReference.ForCustomer("demouser@microsoft.com"));
    }

    [Fact]
    public void ForSubscription_IncludesProductHandle()
    {
        Assert.Equal(
            "eshop:demouser@microsoft.com:eshop-pro",
            BillingReference.ForSubscription("demouser@microsoft.com", "eshop-pro"));
    }

    [Fact]
    public void CentsToAmount_ConvertsWholeDollars()
    {
        Assert.Equal(299.00m, BillingReference.CentsToAmount(29900));
        Assert.Equal(0.01m, BillingReference.CentsToAmount(1));
    }
}
