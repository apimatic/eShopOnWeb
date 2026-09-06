using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

public class SubscriberIdentityTests
{
    [Fact]
    public void DerivesAStableNamespacedReferenceFromTheUserName()
    {
        var first = SubscriberIdentity.ForUser("demouser@microsoft.com");
        var second = SubscriberIdentity.ForUser("  DemoUser@Microsoft.com  ");

        Assert.Equal("eshoponweb-demouser@microsoft.com", first.Reference);

        // Same shopper, different casing/whitespace: the key must not change, or a restart would create a
        // second Maxio customer for the same person.
        Assert.Equal(first.Reference, second.Reference);
    }

    [Fact]
    public void UsesTheUserNameAsTheEmailWhenItIsOne()
    {
        var identity = SubscriberIdentity.ForUser("demouser@microsoft.com");

        Assert.Equal("demouser@microsoft.com", identity.Email);
        Assert.Equal("Demouser", identity.FirstName);
        Assert.Equal("Customer", identity.LastName);
    }

    [Fact]
    public void SplitsADottedLocalPartIntoFirstAndLastName()
    {
        var identity = SubscriberIdentity.ForUser("ada.lovelace@example.com");

        Assert.Equal("Ada", identity.FirstName);
        Assert.Equal("Lovelace", identity.LastName);
    }

    [Fact]
    public void PrefersSuppliedNamesOverDerivedOnes()
    {
        var identity = SubscriberIdentity.ForUser("demouser@microsoft.com", "Grace", "Hopper");

        Assert.Equal("Grace", identity.FirstName);
        Assert.Equal("Hopper", identity.LastName);

        // The reference is never influenced by caller-supplied data.
        Assert.Equal("eshoponweb-demouser@microsoft.com", identity.Reference);
    }

    [Fact]
    public void SynthesisesAnEmailWhenTheUserNameIsNotOne()
    {
        var identity = SubscriberIdentity.ForUser("shopper42");

        Assert.Equal("shopper42@eshoponweb.local", identity.Email);
        Assert.Equal("eshoponweb-shopper42", identity.Reference);
    }
}
