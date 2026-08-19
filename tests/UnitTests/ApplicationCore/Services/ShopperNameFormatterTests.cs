using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ShopperNameFormatterTests
{
    [Fact]
    public void SplitsDottedLocalPartIntoFirstAndLastName()
    {
        var shopper = new ShopperIdentity
        {
            UserId = "1",
            UserName = "jane.doe@microsoft.com",
            Email = "jane.doe@microsoft.com"
        };

        var (first, last) = ShopperNameFormatter.FromIdentity(shopper);

        Assert.Equal("Jane", first);
        Assert.Equal("Doe", last);
    }

    [Fact]
    public void UsesShopperFallbackWhenOnlyOneTokenExists()
    {
        var shopper = new ShopperIdentity
        {
            UserId = "1",
            UserName = "demouser@microsoft.com",
            Email = "demouser@microsoft.com"
        };

        var (first, last) = ShopperNameFormatter.FromIdentity(shopper);

        Assert.Equal("Demouser", first);
        Assert.Equal("Shopper", last);
    }
}
