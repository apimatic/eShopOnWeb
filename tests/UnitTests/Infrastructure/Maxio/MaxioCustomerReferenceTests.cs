using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioCustomerReferenceTests
{
    [Fact]
    public void IsStableForTheSameUser()
    {
        // Stability is the whole idempotency guarantee: the reference is how a returning shopper is
        // recognised, and it has to survive a restart that regenerates every identity key.
        Assert.Equal(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void IgnoresCasingAndSurroundingWhitespace(string variant)
    {
        Assert.Equal(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For(variant));
    }

    [Fact]
    public void DistinguishesDifferentUsers()
    {
        Assert.NotEqual(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For("admin@microsoft.com"));
    }

    [Fact]
    public void AppliesTheConfiguredPrefixSoDeploymentsCanShareASite()
    {
        Assert.Equal("staging-demouser@microsoft.com", MaxioCustomerReference.For("demouser@microsoft.com", "staging"));
        Assert.Equal("eshoponweb-demouser@microsoft.com", MaxioCustomerReference.For("demouser@microsoft.com"));
    }

    [Fact]
    public void FoldsAnOverlongIdentityIntoADigestRatherThanTruncating()
    {
        // Truncation would let two long addresses collide onto one customer.
        var longUser = new string('a', 300) + "@microsoft.com";
        var other = new string('a', 299) + "b@microsoft.com";

        var reference = MaxioCustomerReference.For(longUser);

        Assert.True(reference.Length <= 100);
        Assert.StartsWith("eshoponweb-", reference);
        Assert.NotEqual(reference, MaxioCustomerReference.For(other));
        Assert.Equal(reference, MaxioCustomerReference.For(longUser));
    }

    [Fact]
    public void RejectsAnEmptyIdentity()
    {
        Assert.Throws<ArgumentException>(() => MaxioCustomerReference.For("  "));
    }
}
