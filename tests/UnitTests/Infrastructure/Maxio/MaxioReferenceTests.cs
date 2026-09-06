using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// References are the integration's idempotency anchor, so they must be a pure function of the
/// shopper and plan — the same inputs have to produce the same reference in a later process.
/// </summary>
public class MaxioReferenceTests
{
    private const string Prefix = "eshoponweb";

    [Fact]
    public void CustomerReferenceIsStableForTheSameShopper()
    {
        var first = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");
        var second = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CustomerReferenceIsCaseAndWhitespaceInsensitive()
    {
        var canonical = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");

        Assert.Equal(canonical, MaxioReference.ForCustomer(Prefix, "  DemoUser@Microsoft.COM  "));
    }

    [Fact]
    public void CustomerReferenceIsReadableAndNamespaced()
    {
        var reference = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");

        Assert.StartsWith("eshoponweb-demouser-microsoft-com-", reference);
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "other@microsoft.com")]
    // Slugging flattens punctuation, so these two logins collapse to the same readable slug. The
    // hash suffix is what keeps them from sharing a billing customer.
    [InlineData("a.b@example.com", "a-b@example.com")]
    public void DifferentShoppersGetDifferentReferences(string first, string second)
    {
        Assert.NotEqual(MaxioReference.ForCustomer(Prefix, first), MaxioReference.ForCustomer(Prefix, second));
    }

    [Fact]
    public void DifferentPrefixesNamespaceTheSameShopperApart()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com"),
            MaxioReference.ForCustomer("otherapp", "demouser@microsoft.com"));
    }

    [Fact]
    public void SubscriptionReferenceCombinesCustomerAndPlan()
    {
        var reference = MaxioReference.ForSubscription("eshoponweb-demo-abcd1234", "eshop-pro", attempt: 0);

        Assert.Equal("eshoponweb-demo-abcd1234--eshop-pro", reference);
    }

    [Fact]
    public void SubscriptionReferenceGetsASlotSuffixAfterTheFirstAttempt()
    {
        var first = MaxioReference.ForSubscription("eshoponweb-demo-abcd1234", "eshop-pro", attempt: 0);
        var second = MaxioReference.ForSubscription("eshoponweb-demo-abcd1234", "eshop-pro", attempt: 1);

        Assert.Equal(first + "--r1", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SubscriptionReferencesDifferPerPlan()
    {
        Assert.NotEqual(
            MaxioReference.ForSubscription("eshoponweb-demo-abcd1234", "eshop-pro", 0),
            MaxioReference.ForSubscription("eshoponweb-demo-abcd1234", "basic-plan", 0));
    }

    [Fact]
    public void EachSubscribeAttemptGetsItsOwnUniquenessToken()
    {
        // Maxio remembers a token for 60 minutes whether or not the request it guarded succeeded, so
        // reusing one across attempts would lock a shopper out for an hour after a failed try. The
        // token only has to make a single POST safe to replay.
        var token = MaxioReference.NewUniquenessToken();

        Assert.NotEqual(token, MaxioReference.NewUniquenessToken());
        Assert.True(System.Guid.TryParse(token, out _));
    }
}
