using Microsoft.eShopWeb.MaxioBilling.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

public class SubscriberIdentityTests
{
    [Fact]
    public void ReferenceIsStableForTheSameLoginRegardlessOfCasingOrPadding()
    {
        var first = SubscriberIdentity.ForUser("DemoUser@Microsoft.com");
        var second = SubscriberIdentity.ForUser("  demouser@microsoft.com  ");

        // Idempotency of "ensure a customer exists" rests entirely on this: two requests from the
        // same user must produce the same Maxio customer reference.
        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal("eshoponweb-demouser@microsoft.com", first.Reference);
    }

    [Fact]
    public void DifferentUsersGetDifferentReferences()
    {
        Assert.NotEqual(
            SubscriberIdentity.ForUser("alice@example.com").Reference,
            SubscriberIdentity.ForUser("bob@example.com").Reference);
    }

    [Fact]
    public void ReferenceIsPrefixedSoEShopOnWebCustomersAreDistinguishableOnASharedSite()
    {
        Assert.StartsWith(SubscriberIdentity.ReferencePrefix, SubscriberIdentity.ForUser("someone@example.com").Reference);
    }

    [Theory]
    // Maxio requires a first and last name, but eShopOnWeb stores only a login.
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("grace_brewster_hopper@example.com", "Grace", "Brewster Hopper")]
    public void NamesAreDerivedDeterministicallyFromTheLogin(string login, string expectedFirst, string expectedLast)
    {
        var identity = SubscriberIdentity.ForUser(login);

        Assert.Equal(expectedFirst, identity.FirstName);
        Assert.Equal(expectedLast, identity.LastName);
        Assert.Equal(login, identity.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingLoginIsRejected(string login)
    {
        Assert.Throws<ArgumentException>(() => SubscriberIdentity.ForUser(login));
    }
}
