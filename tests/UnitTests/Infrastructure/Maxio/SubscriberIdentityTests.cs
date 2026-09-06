using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriberIdentityTests
{
    [Fact]
    public void SuppliedNamesWin()
    {
        var subscriber = new SubscriberIdentity("jane", "jane.doe@example.com", "Jane", "Doe");

        Assert.Equal("Jane", subscriber.ResolvedFirstName);
        Assert.Equal("Doe", subscriber.ResolvedLastName);
    }

    [Fact]
    public void SplitsAStructuredEmailLocalPartIntoNames()
    {
        var subscriber = new SubscriberIdentity("jane.doe@example.com", "jane.doe@example.com");

        Assert.Equal("Jane", subscriber.ResolvedFirstName);
        Assert.Equal("Doe", subscriber.ResolvedLastName);
    }

    [Fact]
    public void FallsBackToTheMailDomainWhenThereIsNoFamilyNameToFind()
    {
        var subscriber = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal("Demouser", subscriber.ResolvedFirstName);
        Assert.Equal("Microsoft", subscriber.ResolvedLastName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsAnUnidentifiableSubscriber(string? userName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubscriberIdentity(userName!, "someone@example.com"));
    }
}
