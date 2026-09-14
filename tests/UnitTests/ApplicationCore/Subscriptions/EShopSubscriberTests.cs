using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class EShopSubscriberTests
{
    [Fact]
    public void FromUserName_UsesUserNameAsStableReferenceAndEmail()
    {
        var subscriber = EShopSubscriber.FromUserName("demouser@microsoft.com");

        // Reference and email are the stable identity that maps one eShop user to one Maxio customer.
        Assert.Equal("demouser@microsoft.com", subscriber.Reference);
        Assert.Equal("demouser@microsoft.com", subscriber.Email);
        Assert.Equal("demouser", subscriber.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(subscriber.LastName));
    }

    [Fact]
    public void FromUserName_HandlesNonEmailUserName()
    {
        var subscriber = EShopSubscriber.FromUserName("plainuser");

        Assert.Equal("plainuser", subscriber.Reference);
        Assert.Equal("plainuser", subscriber.FirstName);
    }
}
