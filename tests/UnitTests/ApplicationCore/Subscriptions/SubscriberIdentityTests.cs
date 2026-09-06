using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("jane_doe@example.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@example.com", "Jane", "Van doe")]
    [InlineData("jane+promo@example.com", "Jane", "Shopper")]
    [InlineData("demouser@microsoft.com", "Demouser", "Shopper")]
    public void DeriveNameSplitsTheLocalPartIntoNonBlankNames(string email, string expectedFirst, string expectedLast)
    {
        // Maxio rejects blank names, so the derivation must always yield two usable values for
        // accounts that carry nothing but a login.
        var (first, last) = SubscriberIdentity.DeriveName(email, email);

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Fact]
    public void IdentityCarriesTheBillingReferenceForItsUserName()
    {
        var subscriber = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com", "Demo", "User");

        Assert.Equal(BillingReferences.ForUser("demouser@microsoft.com"), subscriber.BillingReference);
    }
}
