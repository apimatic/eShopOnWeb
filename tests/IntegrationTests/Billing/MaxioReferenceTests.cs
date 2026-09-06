#nullable enable

using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioReferenceTests
{
    [Fact]
    public void ForCustomer_IsStableForTheSameLoginName()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("demouser@microsoft.com"));
    }

    /// <summary>
    /// The reference is derived from the login name rather than a database key precisely so that a
    /// reseeded identity store - which hands the same shopper a new key - does not produce a
    /// second billing customer.
    /// </summary>
    [Fact]
    public void ForCustomer_IgnoresCasingAndSurroundingWhitespace()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("  DemoUser@Microsoft.COM  "));
    }

    [Fact]
    public void ForCustomer_DiffersBetweenShoppers()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("admin@microsoft.com"));
    }

    [Theory]
    [InlineData("demouser@microsoft.com")]
    [InlineData("a.very.long.login.name.that.keeps.going.and.going@example-domain.co.uk")]
    [InlineData("weird+name!with#symbols@example.com")]
    [InlineData("ünicode.näme@example.com")]
    public void ForCustomer_ProducesAUrlSafeBoundedValue(string userName)
    {
        var reference = MaxioReference.ForCustomer(userName);

        Assert.InRange(reference.Length, 1, 100);
        Assert.All(reference, character =>
            Assert.True(char.IsAsciiLetterOrDigit(character) || character == '-',
                $"unexpected character '{character}' in '{reference}'"));
        Assert.Equal(Uri.EscapeDataString(reference), reference);
    }

    /// <summary>
    /// Two logins that slugify to the same readable text must still get different references -
    /// otherwise one shopper would be billed against the other's customer record.
    /// </summary>
    [Fact]
    public void ForCustomer_DoesNotCollideWhenTheReadableSlugIsIdentical()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("first.last@example.com"),
            MaxioReference.ForCustomer("first-last@example.com"));
    }

    [Fact]
    public void ForCustomer_RejectsAnEmptyLoginName()
    {
        Assert.Throws<ArgumentException>(() => MaxioReference.ForCustomer("  "));
    }

    [Fact]
    public void ForSubscription_IsDeterministicPerCustomerAndPlan()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");

        Assert.Equal(
            MaxioReference.ForSubscription(customer, "eshop-pro"),
            MaxioReference.ForSubscription(customer, "eshop-pro"));

        Assert.NotEqual(
            MaxioReference.ForSubscription(customer, "eshop-pro"),
            MaxioReference.ForSubscription(customer, "basic-plan"));
    }

    [Fact]
    public void ForSubscription_GivesEachAttemptItsOwnReference()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");

        var references = Enumerable.Range(1, 5)
            .Select(attempt => MaxioReference.ForSubscription(customer, "eshop-pro", attempt))
            .ToArray();

        Assert.Equal(references.Length, references.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.StartsWith(references[0], references[1], StringComparison.Ordinal);
    }
}
