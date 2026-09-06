using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferenceFactoryTests
{
    private readonly MaxioReferenceFactory _references = new("eshoponweb");

    [Fact]
    public void DerivesTheSameCustomerReferenceEveryTime()
    {
        Assert.Equal(
            _references.ForCustomer("shopper@example.com"),
            _references.ForCustomer("shopper@example.com"));
    }

    [Theory]
    [InlineData("SHOPPER@EXAMPLE.COM")]
    [InlineData("  shopper@example.com  ")]
    [InlineData("Shopper@Example.com")]
    public void TreatsCasingAndSurroundingSpaceAsTheSameShopper(string variant)
    {
        Assert.Equal(_references.ForCustomer("shopper@example.com"), _references.ForCustomer(variant));
    }

    [Fact]
    public void GivesDifferentShoppersDifferentCustomerReferences()
    {
        Assert.NotEqual(_references.ForCustomer("a@example.com"), _references.ForCustomer("b@example.com"));
    }

    [Fact]
    public void GivesEachPlanItsOwnSubscriptionReference()
    {
        Assert.NotEqual(
            _references.ForSubscription("shopper@example.com", "pro", 1),
            _references.ForSubscription("shopper@example.com", "basic", 1));
    }

    [Fact]
    public void GivesEachEnrolmentInTheSamePlanItsOwnReference()
    {
        // Re-subscribing after cancelling must be allowed, so sequence 2 cannot collide with sequence 1.
        Assert.NotEqual(
            _references.ForSubscription("shopper@example.com", "pro", 1),
            _references.ForSubscription("shopper@example.com", "pro", 2));
    }

    [Fact]
    public void GivesAReplayOfTheSameEnrolmentTheSameReference()
    {
        Assert.Equal(
            _references.ForSubscription("shopper@example.com", "pro", 1),
            _references.ForSubscription("shopper@example.com", "pro", 1));
    }

    [Fact]
    public void ScopesCallerSuppliedIdempotencyKeysToTheShopper()
    {
        Assert.NotEqual(
            _references.ForSubscription("a@example.com", "order-99"),
            _references.ForSubscription("b@example.com", "order-99"));
    }

    [Fact]
    public void GivesTheSameIdempotencyKeyTheSameReference()
    {
        Assert.Equal(
            _references.ForSubscription("shopper@example.com", "order-99"),
            _references.ForSubscription("shopper@example.com", "order-99"));
    }

    [Fact]
    public void SeparatesIdempotencyKeysFromPlanSequences()
    {
        Assert.NotEqual(
            _references.ForSubscription("shopper@example.com", "pro"),
            _references.ForSubscription("shopper@example.com", "pro", 1));
    }

    [Fact]
    public void StaysInsideTheLengthAdvancedBillingAccepts()
    {
        var longEmail = new string('a', 240) + "@example.com";

        Assert.True(_references.ForCustomer(longEmail).Length <= MaxioReferenceFactory.MaxReferenceLength);
        Assert.True(_references.ForSubscription(longEmail, new string('p', 120), 3).Length
            <= MaxioReferenceFactory.MaxReferenceLength);
    }

    [Fact]
    public void KeepsOverlongInputsDistinctInsteadOfTruncatingThemTogether()
    {
        // Truncation would let two shoppers share a reference, and so share a subscription.
        var first = new string('a', 240) + "1@example.com";
        var second = new string('a', 240) + "2@example.com";

        Assert.NotEqual(_references.ForCustomer(first), _references.ForCustomer(second));
    }

    [Fact]
    public void HashesDeterministicallyWhenItHasToCollapse()
    {
        var longEmail = new string('a', 260) + "@example.com";

        Assert.Equal(_references.ForCustomer(longEmail), _references.ForCustomer(longEmail));
    }

    [Fact]
    public void RecognisesItsOwnReferences()
    {
        Assert.True(_references.IsOwned(_references.ForCustomer("shopper@example.com")));
        Assert.False(_references.IsOwned("some-other-systems-reference"));
        Assert.False(_references.IsOwned(null));
    }

    [Fact]
    public void KeepsPrefixesFromDifferentDeploymentsApart()
    {
        var staging = new MaxioReferenceFactory("eshoponweb-staging");

        Assert.NotEqual(_references.ForCustomer("shopper@example.com"), staging.ForCustomer("shopper@example.com"));
        Assert.False(staging.IsOwned(_references.ForCustomer("shopper@example.com")));
    }
}
