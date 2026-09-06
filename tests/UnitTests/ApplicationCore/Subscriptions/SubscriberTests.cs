using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberTests
{
    [Fact]
    public void DerivesAStableCustomerReferenceFromTheUserName()
    {
        var subscriber = new Subscriber("demouser@microsoft.com");

        Assert.Equal("eshoponweb--demouser-microsoft-com", subscriber.CustomerReference);
    }

    [Fact]
    public void ProducesTheSameCustomerReferenceForTheSameUserRegardlessOfCasingOrSpacing()
    {
        var first = new Subscriber("demouser@microsoft.com");
        var second = new Subscriber("  DemoUser@Microsoft.com  ");

        Assert.Equal(first.CustomerReference, second.CustomerReference);
    }

    [Fact]
    public void GivesDifferentUsersDifferentCustomerReferences()
    {
        var demo = new Subscriber("demouser@microsoft.com");
        var admin = new Subscriber("admin@microsoft.com");

        Assert.NotEqual(demo.CustomerReference, admin.CustomerReference);
    }

    [Fact]
    public void DerivesTheFirstSubscriptionReferenceFromTheCustomerAndPlan()
    {
        var subscriber = new Subscriber("demouser@microsoft.com");

        Assert.Equal(
            "eshoponweb--demouser-microsoft-com--eshop-pro",
            subscriber.SubscriptionReference("eshop-pro"));
    }

    [Fact]
    public void SuffixesLaterSubscriptionReferencesSoAShopperCanResubscribeToAPlan()
    {
        var subscriber = new Subscriber("demouser@microsoft.com");

        Assert.Equal(
            "eshoponweb--demouser-microsoft-com--eshop-pro--2",
            subscriber.SubscriptionReference("eshop-pro", attempt: 2));
    }

    [Fact]
    public void DefaultsEmailToTheUserNameWhenNoEmailClaimIsPresent()
    {
        var subscriber = new Subscriber("demouser@microsoft.com");

        Assert.Equal("demouser@microsoft.com", subscriber.Email);
    }

    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@example.com", "Jane", "Van Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    public void DerivesAPresentableNameFromTheEmailWhenTheCallerSuppliesNone(
        string email, string expectedFirstName, string expectedLastName)
    {
        var subscriber = new Subscriber(email);

        Assert.Equal(expectedFirstName, subscriber.FirstName);
        Assert.Equal(expectedLastName, subscriber.LastName);
    }

    [Fact]
    public void PrefersTheSuppliedNameOverTheDerivedOne()
    {
        var subscriber = new Subscriber("demouser@microsoft.com", firstName: "Ada", lastName: "Lovelace");

        Assert.Equal("Ada", subscriber.FirstName);
        Assert.Equal("Lovelace", subscriber.LastName);
    }

    [Fact]
    public void RejectsAnEmptyUserName()
    {
        Assert.ThrowsAny<System.ArgumentException>(() => new Subscriber("   "));
    }
}
