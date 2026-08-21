using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class ShopperIdentityTests
{
    [Fact]
    public void BuildsStableCustomerReferenceFromUserId()
    {
        var shopper = new ShopperIdentity("abc-123", "demouser@microsoft.com", "demouser@microsoft.com");
        Assert.Equal("eshop:abc-123", shopper.CustomerReference);
        Assert.Equal("eshop:abc-123:eshop-pro", shopper.SubscriptionReference("eshop-pro"));
    }

    [Fact]
    public void SplitsDottedEmailLocalPartIntoNames()
    {
        var shopper = new ShopperIdentity("id", "demo.user@microsoft.com", null);
        Assert.Equal("Demo", shopper.FirstName);
        Assert.Equal("User", shopper.LastName);
    }
}
