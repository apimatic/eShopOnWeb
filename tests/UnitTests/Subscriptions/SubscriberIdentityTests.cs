using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void FromUser_UsesLowercasedUserNameAsStableReference()
    {
        var identity = SubscriberIdentity.FromUser("Demouser@Microsoft.com");

        Assert.Equal("demouser@microsoft.com", identity.Reference);
        Assert.Equal("Demouser@Microsoft.com", identity.Email);
    }

    [Fact]
    public void FromUser_DerivesFirstNameFromEmailLocalPart()
    {
        var identity = SubscriberIdentity.FromUser("demouser@microsoft.com");

        Assert.Equal("Demouser", identity.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(identity.LastName));
    }

    [Fact]
    public void FromUser_PrefersExplicitEmailWhenProvided()
    {
        var identity = SubscriberIdentity.FromUser("someuser", email: "real@example.com");

        Assert.Equal("real@example.com", identity.Email);
        Assert.Equal("someuser", identity.Reference);
    }
}
