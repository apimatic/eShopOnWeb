using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class BillingCustomerNamingTests
{
    [Fact]
    public void PrefersTheNamesTheCallerSupplied()
    {
        var (first, last) = BillingCustomerNaming.Derive("demouser@microsoft.com", "Ada", "Lovelace");

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Theory]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada_lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada-b-lovelace@example.com", "Ada", "B Lovelace")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    public void DerivesNonBlankNamesFromTheUserName(string userName, string expectedFirst, string expectedLast)
    {
        var (first, last) = BillingCustomerNaming.Derive(userName, null, "   ");

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Fact]
    public void NeverProducesABlankName()
    {
        // Maxio rejects a customer with a blank first or last name.
        var (first, last) = BillingCustomerNaming.Derive("@example.com", null, null);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(last));
    }
}

public class BillingCustomerReferenceTests
{
    [Fact]
    public void IsStableAndCaseInsensitiveForTheSameShopper()
    {
        Assert.Equal(
            BillingCustomerReference.ForUser("demouser@microsoft.com"),
            BillingCustomerReference.ForUser("  DemoUser@Microsoft.COM  "));
    }

    [Fact]
    public void DistinguishesDifferentShoppers()
    {
        Assert.NotEqual(
            BillingCustomerReference.ForUser("a@example.com"),
            BillingCustomerReference.ForUser("b@example.com"));
    }
}

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData(SubscriptionStates.Active)]
    [InlineData(SubscriptionStates.Trialing)]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.OnHold)]
    [InlineData("a_state_maxio_has_not_invented_yet")]
    public void TreatsAnythingNonTerminalAsStillHeld(string state) =>
        Assert.True(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData(SubscriptionStates.Canceled)]
    [InlineData(SubscriptionStates.Expired)]
    [InlineData(SubscriptionStates.FailedToCreate)]
    [InlineData("")]
    [InlineData(null)]
    public void TreatsTerminalStatesAsNoLongerHeld(string? state) =>
        Assert.False(SubscriptionStates.IsLive(state));
}
