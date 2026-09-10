using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void UsesUserNameAsReferenceAndEmail()
    {
        var identity = SubscriberIdentity.FromUserName("demouser@microsoft.com");

        Assert.Equal("demouser@microsoft.com", identity.Reference);
        Assert.Equal("demouser@microsoft.com", identity.Email);
    }

    [Fact]
    public void DerivesFirstAndLastNameFromDottedLocalPart()
    {
        var identity = SubscriberIdentity.FromUserName("jane.doe@example.com");

        Assert.Equal("Jane", identity.FirstName);
        Assert.Equal("Doe", identity.LastName);
    }

    [Fact]
    public void FallsBackToPlaceholderLastNameForSimpleLocalPart()
    {
        var identity = SubscriberIdentity.FromUserName("demouser@microsoft.com");

        Assert.Equal("Demouser", identity.FirstName);
        Assert.Equal("eShopOnWeb", identity.LastName);
    }
}
