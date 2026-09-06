using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferenceTests
{
    [Fact]
    public void ForCustomer_IsDeterministic()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "  DemoUser@Microsoft.com  ")]
    [InlineData("a@b.com", "A@B.COM")]
    public void ForCustomer_IgnoresCaseAndSurroundingWhitespace(string left, string right)
    {
        Assert.Equal(MaxioReference.ForCustomer(left), MaxioReference.ForCustomer(right));
    }

    [Fact]
    public void ForCustomer_DistinguishesDifferentUsers()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("admin@microsoft.com"));
    }

    [Fact]
    public void ForCustomer_DistinguishesUsersThatSlugifyIdentically()
    {
        // Both fold to the same slug; only the hash suffix keeps them apart.
        var first = MaxioReference.ForCustomer("a.b@example.com");
        var second = MaxioReference.ForCustomer("a-b@example.com");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForCustomer_DistinguishesUsersSharingALongPrefix()
    {
        var prefix = new string('u', 60);

        Assert.NotEqual(
            MaxioReference.ForCustomer($"{prefix}1@example.com"),
            MaxioReference.ForCustomer($"{prefix}2@example.com"));
    }

    [Fact]
    public void ForCustomer_IsPrefixedSoRecordsAreAttributable()
    {
        Assert.StartsWith("eshoponweb-", MaxioReference.ForCustomer("demouser@microsoft.com"));
    }

    [Fact]
    public void ForSubscription_IsScopedToTheCustomer()
    {
        var customerReference = MaxioReference.ForCustomer("demouser@microsoft.com");

        var subscriptionReference = MaxioReference.ForSubscription(customerReference, "pro-plan");

        Assert.StartsWith(customerReference, subscriptionReference);
    }

    [Fact]
    public void ForSubscription_IsDeterministicPerIdempotencyKey()
    {
        var customerReference = MaxioReference.ForCustomer("demouser@microsoft.com");

        Assert.Equal(
            MaxioReference.ForSubscription(customerReference, "pro-plan"),
            MaxioReference.ForSubscription(customerReference, "pro-plan"));
    }

    [Fact]
    public void ForSubscription_DiffersPerIdempotencyKey()
    {
        var customerReference = MaxioReference.ForCustomer("demouser@microsoft.com");

        Assert.NotEqual(
            MaxioReference.ForSubscription(customerReference, "pro-plan"),
            MaxioReference.ForSubscription(customerReference, "starter-plan"));
    }
}
