using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class BillingUserTests
{
    [Fact]
    public void FromUserName_UsesEmailAsStableReferenceAndEmail()
    {
        var user = BillingUser.FromUserName("demouser@microsoft.com");

        Assert.Equal("demouser@microsoft.com", user.Reference);
        Assert.Equal("demouser@microsoft.com", user.Email);
        Assert.Equal("demouser", user.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(user.LastName));
    }

    [Fact]
    public void FromUserName_TrimsWhitespace()
    {
        var user = BillingUser.FromUserName("  someone@example.com  ");

        Assert.Equal("someone@example.com", user.Reference);
    }

    [Fact]
    public void FromUserName_HandlesNonEmailUserName()
    {
        var user = BillingUser.FromUserName("plainusername");

        Assert.Equal("plainusername", user.Reference);
        Assert.Equal("plainusername", user.FirstName);
    }
}
