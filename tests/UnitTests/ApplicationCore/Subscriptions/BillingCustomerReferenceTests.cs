using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class BillingCustomerReferenceTests
{
    [Fact]
    public void NamespacesTheReferenceSoEShopOnWebCustomersAreRecognisable()
    {
        Assert.StartsWith(BillingCustomerReference.Prefix, BillingCustomerReference.For("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("Demouser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void ProducesTheSameReferenceRegardlessOfCasingOrPadding(string userName)
    {
        Assert.Equal(BillingCustomerReference.For("demouser@microsoft.com"), BillingCustomerReference.For(userName));
    }

    [Fact]
    public void DistinguishesDifferentShoppers()
    {
        Assert.NotEqual(BillingCustomerReference.For("one@example.com"), BillingCustomerReference.For("two@example.com"));
    }

    [Fact]
    public void ReplacesCharactersThatWouldNotSurviveAUrl()
    {
        var reference = BillingCustomerReference.For("a b/c?d@example.com");

        Assert.DoesNotContain(" ", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("/", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("?", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesToBuildAReferenceWithoutAUserName(string userName)
    {
        Assert.Throws<ArgumentException>(() => BillingCustomerReference.For(userName));
    }
}
