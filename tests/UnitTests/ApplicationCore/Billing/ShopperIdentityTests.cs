using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class ShopperIdentityTests
{
    [Fact]
    public void SplitName_UsesEmailLocalParts()
    {
        var (first, last) = ShopperIdentity.SplitName("jane.doe@microsoft.com", null);
        Assert.Equal("Jane", first);
        Assert.Equal("Doe", last);
    }

    [Fact]
    public void SplitName_FallsBackToCustomer()
    {
        var (first, last) = ShopperIdentity.SplitName("demouser@microsoft.com", "demouser@microsoft.com");
        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    [Fact]
    public void FromAccount_UsesUserIdAsCustomerReference()
    {
        var identity = ShopperIdentity.FromAccount("abc-123", "demouser@microsoft.com", "demouser@microsoft.com");
        Assert.Equal("abc-123", identity.UserId);
        Assert.Equal("demouser@microsoft.com", identity.Email);
    }
}
