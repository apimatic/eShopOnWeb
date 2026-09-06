using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class BillingReferencesTests
{
    private const string DemoUser = "demouser@microsoft.com";

    [Fact]
    public void ForUserIsStableAcrossCalls()
    {
        // Stability is the whole point: it is what lets a restarted host with an empty in-memory
        // database find the shopper's existing billing customer again.
        Assert.Equal(BillingReferences.ForUser(DemoUser), BillingReferences.ForUser(DemoUser));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void ForUserIgnoresCaseAndSurroundingWhitespace(string variant)
    {
        Assert.Equal(BillingReferences.ForUser(DemoUser), BillingReferences.ForUser(variant));
    }

    [Fact]
    public void ForUserSeparatesDifferentUsers()
    {
        Assert.NotEqual(BillingReferences.ForUser(DemoUser), BillingReferences.ForUser("admin@microsoft.com"));
    }

    [Fact]
    public void ForUserSeparatesUsersThatSlugifyIdentically()
    {
        // "a.b@x.com" and "a-b@x.com" reduce to the same slug, so only the hash keeps them apart.
        var first = BillingReferences.ForUser("a.b@x.com");
        var second = BillingReferences.ForUser("a-b@x.com");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("demouser@microsoft.com")]
    [InlineData("Ünïcödé Shopper!@example.com")]
    [InlineData("a-very-long-login-name-that-goes-well-past-the-slug-budget@some-domain.example.com")]
    public void ForUserProducesASafeBoundedReference(string userName)
    {
        var reference = BillingReferences.ForUser(userName);

        Assert.Matches(new Regex("^eshop-[a-z0-9-]+$"), reference);
        Assert.DoesNotContain("--", reference);
        Assert.True(reference.Length <= 64, $"Reference was {reference.Length} characters: {reference}");
    }

    [Fact]
    public void ForUserKeepsTheLoginRecognisable()
    {
        Assert.StartsWith("eshop-demouser-microsoft-com-", BillingReferences.ForUser(DemoUser));
    }

    [Fact]
    public void FirstSubscriptionReferenceHasNoOrdinalSuffix()
    {
        Assert.Equal("eshop-someone-eshop-pro", BillingReferences.ForSubscription("eshop-someone", "eshop-pro", 0));
    }

    [Fact]
    public void LaterSubscriptionsToTheSamePlanGetTheNextOrdinal()
    {
        // A shopper who cancels and resubscribes must not collide with their retired subscription.
        Assert.Equal("eshop-someone-eshop-pro-2", BillingReferences.ForSubscription("eshop-someone", "eshop-pro", 1));
        Assert.Equal("eshop-someone-eshop-pro-3", BillingReferences.ForSubscription("eshop-someone", "eshop-pro", 2));
    }

    [Fact]
    public void SubscriptionReferenceIsPlanSpecific()
    {
        Assert.NotEqual(
            BillingReferences.ForSubscription("eshop-someone", "eshop-pro", 0),
            BillingReferences.ForSubscription("eshop-someone", "basic-plan", 0));
    }

    [Fact]
    public void IdempotencyKeyedReferenceIsStableForTheSameKey()
    {
        var first = BillingReferences.ForSubscription("eshop-someone", "eshop-pro", "checkout-42");
        var second = BillingReferences.ForSubscription("eshop-someone", "eshop-pro", " checkout-42 ");

        Assert.Equal(first, second);
        Assert.StartsWith("eshop-someone-eshop-pro-", first);
    }

    [Fact]
    public void IdempotencyKeyedReferenceDiffersPerKey()
    {
        Assert.NotEqual(
            BillingReferences.ForSubscription("eshop-someone", "eshop-pro", "checkout-42"),
            BillingReferences.ForSubscription("eshop-someone", "eshop-pro", "checkout-43"));
    }
}
