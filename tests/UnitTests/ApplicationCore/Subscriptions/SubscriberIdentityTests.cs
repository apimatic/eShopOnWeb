using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    [Theory]
    [InlineData("demouser@microsoft.com", "eshop:demouser@microsoft.com")]
    [InlineData("DemoUser@Microsoft.com", "eshop:demouser@microsoft.com")]
    [InlineData("  demouser@microsoft.com  ", "eshop:demouser@microsoft.com")]
    public void BuildsAStableBillingReferenceFromTheUserName(string userName, string expected)
    {
        var identity = new SubscriberIdentity(userName, "demouser@microsoft.com");

        Assert.Equal(expected, identity.BillingReference);
    }

    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@example.com", "Jane", "Van Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    public void DerivesTheNamesMaxioRequiresFromTheEmail(string email, string firstName, string lastName)
    {
        var identity = new SubscriberIdentity(email, email);

        Assert.Equal(firstName, identity.ResolvedFirstName);
        Assert.Equal(lastName, identity.ResolvedLastName);
    }

    [Fact]
    public void PrefersAnExplicitNameOverTheDerivedOne()
    {
        var identity = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com", "Ada", "Lovelace");

        Assert.Equal("Ada", identity.ResolvedFirstName);
        Assert.Equal("Lovelace", identity.ResolvedLastName);
    }

    [Theory]
    [InlineData(SubscriptionStates.Canceled, true)]
    [InlineData(SubscriptionStates.Expired, true)]
    [InlineData(SubscriptionStates.FailedToCreate, true)]
    [InlineData(SubscriptionStates.TrialEnded, true)]
    [InlineData(SubscriptionStates.Active, false)]
    [InlineData(SubscriptionStates.PastDue, false)]
    [InlineData(SubscriptionStates.OnHold, false)]
    [InlineData(SubscriptionStates.Trialing, false)]
    public void ClassifiesEndOfLifeStatesTheWayTheSpecificationDoes(string state, bool endOfLife)
    {
        Assert.Equal(endOfLife, SubscriptionStates.IsEndOfLife(state));
    }
}
