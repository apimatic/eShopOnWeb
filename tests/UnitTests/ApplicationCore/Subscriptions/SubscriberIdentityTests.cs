using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void DerivesFirstNameFromEmailLocalPart_WhenNotProvided()
    {
        var identity = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal("demouser", identity.FirstName);
        Assert.Equal("eShopOnWeb", identity.LastName);
    }

    [Fact]
    public void UsesProvidedNames_WhenGiven()
    {
        var identity = new SubscriberIdentity("jdoe", "jdoe@example.com", "Jane", "Doe");

        Assert.Equal("Jane", identity.FirstName);
        Assert.Equal("Doe", identity.LastName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_WhenUserNameMissing(string? userName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubscriberIdentity(userName!, "e@example.com"));
    }

    [Fact]
    public void Throws_WhenEmailMissing()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubscriberIdentity("user", ""));
    }
}
