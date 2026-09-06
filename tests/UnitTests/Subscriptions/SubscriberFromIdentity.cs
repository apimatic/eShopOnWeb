using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class SubscriberFromIdentity
{
    [Fact]
    public void DerivesAStablePrefixedReferenceFromTheUserName()
    {
        var subscriber = Subscriber.FromIdentity("DemoUser@microsoft.com");

        Assert.Equal("eshoponweb-demouser@microsoft.com", subscriber.Reference);
    }

    [Fact]
    public void ProducesTheSameReferenceRegardlessOfCasingOrPadding()
    {
        var first = Subscriber.FromIdentity("demouser@microsoft.com");
        var second = Subscriber.FromIdentity("  DEMOUSER@MICROSOFT.COM  ");

        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PrefersTheIdentityEmailOverTheUserName()
    {
        var subscriber = Subscriber.FromIdentity("demouser@microsoft.com", "billing@contoso.com");

        Assert.Equal("billing@contoso.com", subscriber.Email);

        // The reference stays anchored to the user name so it does not drift if the e-mail is edited.
        Assert.Equal("eshoponweb-demouser@microsoft.com", subscriber.Reference);
    }

    [Fact]
    public void FallsBackToTheUserNameWhenNoEmailIsRecorded()
    {
        var subscriber = Subscriber.FromIdentity("demouser@microsoft.com", email: null);

        Assert.Equal("demouser@microsoft.com", subscriber.Email);
    }

    [Theory]
    [InlineData("jane.doe@contoso.com", "Jane", "Doe")]
    [InlineData("jane_van.doe@contoso.com", "Jane", "Van Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    public void DerivesGivenAndFamilyNamesTheBillingSystemRequires(string userName, string expectedFirst, string expectedLast)
    {
        var subscriber = Subscriber.FromIdentity(userName);

        Assert.Equal(expectedFirst, subscriber.FirstName);
        Assert.Equal(expectedLast, subscriber.LastName);
    }
}
