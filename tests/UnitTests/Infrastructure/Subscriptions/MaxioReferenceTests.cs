using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

/// <summary>
/// The references written into Maxio are the integration's idempotency keys, so their determinism is the
/// property worth protecting: the same shopper and plan must always produce the same string.
/// </summary>
public class MaxioReferenceTests
{
    [Fact]
    public void CustomerReferenceIsDeterministic()
    {
        var first = MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com");
        var second = MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com");

        Assert.Equal("eshoponweb:customer:demouser@microsoft.com", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CustomerReferenceIsCaseInsensitive()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("eshoponweb", "DemoUser@Microsoft.com"),
            MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com"));
    }

    [Fact]
    public void PrefixSeparatesApplicationsSharingASite()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com"),
            MaxioReference.ForCustomer("other-app", "demouser@microsoft.com"));
    }

    [Fact]
    public void SubscriptionReferenceIsDeterministicPerUserAndKey()
    {
        var reference = MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "eshop-pro");

        Assert.Equal("eshoponweb:subscription:demouser@microsoft.com:eshop-pro", reference);
        Assert.Equal(reference, MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "eshop-pro"));
    }

    [Fact]
    public void DifferentPlansDoNotCollide()
    {
        Assert.NotEqual(
            MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "eshop-pro"),
            MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "basic-plan"));
    }

    [Fact]
    public void DifferentSubscribersDoNotCollide()
    {
        Assert.NotEqual(
            MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "eshop-pro"),
            MaxioReference.ForSubscription("eshoponweb", "admin@microsoft.com", "eshop-pro"));
    }

    [Theory]
    [InlineData(1, "eshoponweb:subscription:demouser@microsoft.com:eshop-pro")]
    [InlineData(2, "eshoponweb:subscription:demouser@microsoft.com:eshop-pro#2")]
    [InlineData(3, "eshoponweb:subscription:demouser@microsoft.com:eshop-pro#3")]
    public void LaterAttemptsAreSuffixedSoAResubscribeCanSucceed(int attempt, string expected)
    {
        Assert.Equal(expected, MaxioReference.ForSubscription("eshoponweb", "demouser@microsoft.com", "eshop-pro", attempt));
    }

    [Fact]
    public void SeparatorsInInputCannotForgeAnotherSubscribersReference()
    {
        // A user key containing ':' must not be able to impersonate a different key/plan combination.
        var crafted = MaxioReference.ForSubscription("eshoponweb", "attacker:victim@microsoft.com", "eshop-pro");
        var victim = MaxioReference.ForSubscription("eshoponweb", "victim@microsoft.com", "eshop-pro");

        Assert.NotEqual(victim, crafted);
        Assert.Equal("eshoponweb:subscription:attacker-victim@microsoft.com:eshop-pro", crafted);
    }

    [Fact]
    public void LongInputIsTruncatedToWhatMaxioAccepts()
    {
        var reference = MaxioReference.ForSubscription("eshoponweb", new string('u', 400), "eshop-pro");

        Assert.Equal(255, reference.Length);
    }
}
