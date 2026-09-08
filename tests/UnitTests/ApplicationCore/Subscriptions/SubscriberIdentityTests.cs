using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void FromUserName_UsesEmailAsReference_AndDerivesNames()
    {
        var identity = SubscriberIdentity.FromUserName("demouser@microsoft.com");

        Assert.Equal("demouser@microsoft.com", identity.Reference);
        Assert.Equal("demouser@microsoft.com", identity.Email);
        Assert.Equal("demouser", identity.FirstName);
        Assert.Equal("eShopOnWeb", identity.LastName);
    }

    [Fact]
    public void FromUserName_TrimsSurroundingWhitespace()
    {
        var identity = SubscriberIdentity.FromUserName("  someone@example.com  ");

        Assert.Equal("someone@example.com", identity.Reference);
        Assert.Equal("someone", identity.FirstName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromUserName_Throws_WhenIdentityMissing(string? userName)
    {
        Assert.Throws<ArgumentException>(() => SubscriberIdentity.FromUserName(userName!));
    }
}
