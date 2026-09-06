using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class BillingCustomerIdentityTests
{
    [Fact]
    public void ProducesTheSameReferenceEveryTimeForTheSameUser()
    {
        // The whole ensure-customer-once guarantee rests on this being deterministic.
        var first = BillingCustomerIdentity.ForUser("demouser@microsoft.com");
        var second = BillingCustomerIdentity.ForUser("demouser@microsoft.com");

        Assert.Equal(first.Reference, second.Reference);
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void IgnoresCasingAndSurroundingWhitespaceInTheUserName(string userName)
    {
        var identity = BillingCustomerIdentity.ForUser(userName);

        Assert.Equal("eshoponweb-demouser@microsoft.com", identity.Reference);
    }

    [Fact]
    public void GivesDifferentUsersDifferentReferences()
    {
        Assert.NotEqual(
            BillingCustomerIdentity.ForUser("demouser@microsoft.com").Reference,
            BillingCustomerIdentity.ForUser("admin@microsoft.com").Reference);
    }

    [Fact]
    public void NamespacesTheReferenceSoItCannotCollideWithAnotherSystemsRecords()
    {
        Assert.StartsWith(BillingCustomerIdentity.ReferencePrefix, BillingCustomerIdentity.ForUser("a@b.com").Reference);
    }

    [Fact]
    public void AlwaysSuppliesTheNamesTheBillingProviderRequires()
    {
        // eShopOnWeb's identity carries no given/family name, but the provider will not accept a customer
        // without both.
        var identity = BillingCustomerIdentity.ForUser("demouser@microsoft.com");

        Assert.False(string.IsNullOrWhiteSpace(identity.FirstName));
        Assert.False(string.IsNullOrWhiteSpace(identity.LastName));
        Assert.Equal("demouser@microsoft.com", identity.Email);
    }

    [Fact]
    public void DerivesReadableNamesFromAStructuredEmailLocalPart()
    {
        var identity = BillingCustomerIdentity.ForUser("ada.lovelace@example.com");

        Assert.Equal("Ada", identity.FirstName);
        Assert.Equal("Lovelace", identity.LastName);
    }

    [Fact]
    public void PrefersAnExplicitEmailOverTheUserName()
    {
        var identity = BillingCustomerIdentity.ForUser("demouser", "demo.user@example.com");

        Assert.Equal("demo.user@example.com", identity.Email);
        Assert.Equal("eshoponweb-demouser", identity.Reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyUserName(string userName)
    {
        Assert.ThrowsAny<ArgumentException>(() => BillingCustomerIdentity.ForUser(userName));
    }
}
