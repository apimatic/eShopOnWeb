using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void FromEmail_UsesNormalizedEmailAsStableReference()
    {
        var identity = SubscriberIdentity.FromEmail("DemoUser@Microsoft.com");

        // Reference must be stable and lower-cased so the same user maps to one Maxio customer.
        Assert.Equal("demouser@microsoft.com", identity.Reference);
        Assert.Equal("demouser@microsoft.com", identity.Email);
    }

    [Fact]
    public void FromEmail_DerivesFirstNameFromLocalPart()
    {
        var identity = SubscriberIdentity.FromEmail("  Demouser@microsoft.com ");

        Assert.Equal("demouser", identity.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(identity.LastName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromEmail_Throws_WhenEmailMissing(string? email)
    {
        Assert.Throws<ArgumentException>(() => SubscriberIdentity.FromEmail(email!));
    }
}
