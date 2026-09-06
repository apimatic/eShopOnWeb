using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferencesTests
{
    private const string Prefix = "eshoponweb";

    [Fact]
    public void CustomerReferenceIsNamespacedAndDerivedFromTheUserName()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com",
            MaxioReferences.CustomerReference(Prefix, "demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void CustomerReferenceIsStableAcrossCasingAndWhitespace(string userName)
    {
        // Two spellings of the same sign-in must not become two Maxio customers.
        Assert.Equal(
            MaxioReferences.CustomerReference(Prefix, "demouser@microsoft.com"),
            MaxioReferences.CustomerReference(Prefix, userName));
    }

    [Fact]
    public void CustomerReferenceRejectsAnEmptyUserName()
    {
        Assert.Throws<ArgumentException>(() => MaxioReferences.CustomerReference(Prefix, "  "));
    }

    [Fact]
    public void FirstSubscriptionToAPlanUsesTheUnsuffixedReference()
    {
        var root = MaxioReferences.SubscriptionReferenceRoot("eshoponweb:jane@example.com", "pro");

        Assert.Equal("eshoponweb:jane@example.com:pro", root);
        Assert.Equal(root, MaxioReferences.NextAvailableSubscriptionReference(root, Enumerable.Empty<string?>()));
    }

    [Fact]
    public void ResubscribingAfterCancellationTakesTheNextFreeSuffix()
    {
        var root = "eshoponweb:jane@example.com:pro";

        var reference = MaxioReferences.NextAvailableSubscriptionReference(
            root,
            new[] { root, root + ":2", "eshoponweb:jane@example.com:basic" });

        Assert.Equal(root + ":3", reference);
    }

    [Fact]
    public void ReferenceChoiceIgnoresCasingAndMissingReferences()
    {
        var root = "eshoponweb:jane@example.com:pro";

        var reference = MaxioReferences.NextAvailableSubscriptionReference(
            root,
            new[] { null, string.Empty, root.ToUpperInvariant() });

        Assert.Equal(root + ":2", reference);
    }

    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@example.com", "Jane", "Van Doe")]
    [InlineData("jane_doe@example.com", "Jane", "Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "eShopOnWeb")]
    public void CustomerNameIsDerivedFromTheLocalPartOfTheEmail(string userName, string firstName, string lastName)
    {
        var derived = MaxioReferences.DeriveCustomerName(userName);

        Assert.Equal(firstName, derived.FirstName);
        Assert.Equal(lastName, derived.LastName);
    }

    [Fact]
    public void CustomerNameFallsBackWhenThereIsNothingToSplit()
    {
        // Maxio requires both names, so neither may come back empty.
        var derived = MaxioReferences.DeriveCustomerName("@example.com");

        Assert.False(string.IsNullOrWhiteSpace(derived.FirstName));
        Assert.False(string.IsNullOrWhiteSpace(derived.LastName));
    }
}
