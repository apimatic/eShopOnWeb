using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriberIdentityTests
{
    private static SubscriberIdentity For(string email, string? first = null, string? last = null) => new()
    {
        UserId = email,
        Email = email,
        FirstName = first,
        LastName = last
    };

    [Fact]
    public void PrefersTheNamesItWasGiven()
    {
        Assert.Equal(("Ada", "Lovelace"), For("ada@example.com", "Ada", "Lovelace").ResolveName());
    }

    [Theory]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada_lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada-lovelace@example.com", "Ada", "Lovelace")]
    public void SplitsASeparatedEmailLocalPartIntoAName(string email, string first, string last)
    {
        Assert.Equal((first, last), For(email).ResolveName());
    }

    [Fact]
    public void FallsBackToAPlaceholderSurnameWhenTheEmailHasNoSeparator()
    {
        // Maxio requires both names, and eShopOnWeb identities carry neither.
        Assert.Equal(("Demouser", "eShopOnWeb"), For("demouser@microsoft.com").ResolveName());
    }

    [Fact]
    public void KeepsASuppliedFirstNameWhenOnlyTheSurnameIsMissing()
    {
        Assert.Equal(("Ada", "eShopOnWeb"), For("ada.lovelace@example.com", "Ada").ResolveName());
    }
}

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("pending")]
    public void TreatsLiveStatesAsAnExistingEnrollment(string state)
    {
        Assert.True(SubscriptionStates.IsLive(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    [InlineData(null)]
    public void TreatsEndOfLifeStatesAsFreeToReSubscribe(string? state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
    }

    [Fact]
    public void PaymentProblemStatesAreLiveButNotHealthy()
    {
        Assert.True(SubscriptionStates.IsLive("past_due"));
        Assert.False(SubscriptionStates.IsHealthy("past_due"));
    }
}
