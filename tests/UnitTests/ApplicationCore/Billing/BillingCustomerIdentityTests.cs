using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class BillingCustomerIdentityTests
{
    [Fact]
    public void CustomerReferenceIsStableForTheSameShopper()
    {
        var first = BillingCustomerIdentity.FromEmail("demouser@microsoft.com");
        var second = BillingCustomerIdentity.FromEmail("demouser@microsoft.com");

        Assert.Equal(first.CustomerReference, second.CustomerReference);
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void CustomerReferenceIgnoresCasingAndSurroundingWhitespace(string variant)
    {
        var canonical = BillingCustomerIdentity.FromEmail("demouser@microsoft.com");

        Assert.Equal(canonical.CustomerReference, BillingCustomerIdentity.FromEmail(variant).CustomerReference);
    }

    [Fact]
    public void DifferentShoppersGetDifferentCustomerReferences()
    {
        var first = BillingCustomerIdentity.FromEmail("demouser@microsoft.com");
        var second = BillingCustomerIdentity.FromEmail("admin@microsoft.com");

        Assert.NotEqual(first.CustomerReference, second.CustomerReference);
    }

    [Fact]
    public void AddressesThatSlugifyIdenticallyStillGetDifferentReferences()
    {
        // 'a.b' and 'a-b' both slugify to 'a-b'; the hash suffix is what keeps them apart.
        var dotted = BillingCustomerIdentity.FromEmail("a.b@example.com");
        var hyphenated = BillingCustomerIdentity.FromEmail("a-b@example.com");

        Assert.NotEqual(dotted.CustomerReference, hyphenated.CustomerReference);
    }

    [Fact]
    public void SubscriptionReferenceIsPerShopperAndPerPlan()
    {
        var shopper = BillingCustomerIdentity.FromEmail("demouser@microsoft.com");

        var pro = shopper.SubscriptionReference("eshop-pro");
        var basic = shopper.SubscriptionReference("basic-plan");

        Assert.NotEqual(pro, basic);
        Assert.Equal(pro, shopper.SubscriptionReference("eshop-pro"));
        Assert.StartsWith(shopper.CustomerReference, pro, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencesAreSafeToPutInAUrl()
    {
        var shopper = BillingCustomerIdentity.FromEmail("Some.One+tag@Example.co.uk");

        Assert.Matches("^[a-z0-9-]+$", shopper.CustomerReference);
        Assert.Matches("^[a-z0-9-]+$", shopper.SubscriptionReference("eshop-pro"));
    }

    [Theory]
    [InlineData("first.last@example.com", "First", "Last")]
    [InlineData("first_middle_last@example.com", "First", "Middle Last")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    public void NamesAreDerivedFromTheAddressBecauseTheTokenCarriesNoneOfItsOwn(string email, string firstName, string lastName)
    {
        var shopper = BillingCustomerIdentity.FromEmail(email);

        Assert.Equal(firstName, shopper.FirstName);
        Assert.Equal(lastName, shopper.LastName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyAddressIsRejected(string? email)
    {
        Assert.Throws<ArgumentException>(() => BillingCustomerIdentity.FromEmail(email!));
    }
}
