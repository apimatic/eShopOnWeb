using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class MaxioReferenceTests
{
    [Fact]
    public void ForCustomer_UsesStableUserPrefix()
    {
        var reference = MaxioReference.ForCustomer("user-123");

        Assert.Equal("eshop-user:user-123", reference);
    }

    [Fact]
    public void ForSubscription_IncludesUserAndPlan()
    {
        var reference = MaxioReference.ForSubscription("user-123", "eshop-pro");

        Assert.Equal("eshop-sub:user-123:eshop-pro", reference);
    }
}

public class ShopperNameTests
{
    [Fact]
    public void FromProfile_SplitsEmailLocalPart()
    {
        var profile = new ShopperProfile { Email = "demo.user@microsoft.com", UserName = "demo.user@microsoft.com" };

        var (first, last) = ShopperName.FromProfile(profile);

        Assert.Equal("Demo", first);
        Assert.Equal("User", last);
    }

    [Fact]
    public void FromProfile_FallsBackWhenLocalPartIsSingleToken()
    {
        var profile = new ShopperProfile { Email = "demouser@microsoft.com", UserName = "demouser@microsoft.com" };

        var (first, last) = ShopperName.FromProfile(profile);

        Assert.Equal("Demouser", first);
        Assert.Equal("Shopper", last);
    }
}
