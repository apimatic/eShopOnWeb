using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Fact]
    public void BuildsTheSameCustomerReferenceForTheSameShopperEveryTime()
    {
        var first = SubscriberIdentity.BuildCustomerReference("eshoponweb", "Demouser@Microsoft.com");
        var second = SubscriberIdentity.BuildCustomerReference("eshoponweb", "demouser@microsoft.com ");

        Assert.Equal("eshoponweb-demouser@microsoft.com", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void OmitsTheSeparatorWhenNoPrefixIsConfigured()
    {
        Assert.Equal("a@b.com", SubscriberIdentity.BuildCustomerReference(string.Empty, "a@b.com"));
    }

    [Theory]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada_lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada-b-lovelace@example.com", "Ada", "B Lovelace")]
    [InlineData("demouser@microsoft.com", "Demouser", "Microsoft")]
    public void DerivesANonBlankNameFromTheEmailBecauseTheProviderRequiresBoth(string email, string first, string last)
    {
        var identity = new SubscriberIdentity(email, email, "ref");

        Assert.Equal(first, identity.FirstName);
        Assert.Equal(last, identity.LastName);
    }

    [Fact]
    public void PrefersAnExplicitNameOverTheDerivedOne()
    {
        var identity = new SubscriberIdentity("a@b.com", "a@b.com", "ref", "Grace", "Hopper");

        Assert.Equal("Grace", identity.FirstName);
        Assert.Equal("Hopper", identity.LastName);
    }

    [Fact]
    public void RejectsAnIdentityWithNoEmailToBill()
    {
        Assert.Throws<ArgumentException>(() => new SubscriberIdentity("user", "  ", "ref"));
    }
}
