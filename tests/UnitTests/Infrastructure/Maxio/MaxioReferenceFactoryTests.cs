using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferenceFactoryTests
{
    private readonly MaxioReferenceFactory _factory = new("eshoponweb");

    [Fact]
    public void CustomerReferenceIsStableAcrossCalls()
    {
        Assert.Equal(
            _factory.CustomerReference("demouser@microsoft.com"),
            _factory.CustomerReference("demouser@microsoft.com"));
    }

    [Fact]
    public void CustomerReferenceIgnoresCasingAndSurroundingWhitespace()
    {
        Assert.Equal(
            "eshoponweb-demouser@microsoft.com",
            _factory.CustomerReference("  DemoUser@Microsoft.com "));
    }

    [Fact]
    public void CustomerReferencesDifferPerShopper()
    {
        Assert.NotEqual(
            _factory.CustomerReference("a@example.com"),
            _factory.CustomerReference("b@example.com"));
    }

    [Fact]
    public void CustomerReferenceFallsBackToAHashForVeryLongKeys()
    {
        var longKey = new string('a', 300) + "@example.com";

        var reference = _factory.CustomerReference(longKey);

        Assert.StartsWith("eshoponweb-", reference);
        Assert.True(reference.Length <= 100);
        Assert.Equal(reference, _factory.CustomerReference(longKey));
    }

    [Fact]
    public void FirstSubscriptionReferenceIsTheBareCustomerAndPlanPair()
    {
        var customerReference = _factory.CustomerReference("demouser@microsoft.com");

        Assert.Equal(
            "eshoponweb-demouser@microsoft.com-eshop-pro",
            _factory.SubscriptionReference(customerReference, "eshop-pro", generation: 0));
    }

    [Fact]
    public void ResubscribingProducesADistinctReference()
    {
        var customerReference = _factory.CustomerReference("demouser@microsoft.com");

        var first = _factory.SubscriptionReference(customerReference, "eshop-pro", generation: 0);
        var second = _factory.SubscriptionReference(customerReference, "eshop-pro", generation: 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void UniquenessTokenIsStableForTheSameAttempt()
    {
        Assert.Equal(
            _factory.UniquenessToken("subscription", "cust", "eshop-pro", "0"),
            _factory.UniquenessToken("subscription", "cust", "eshop-pro", "0"));
    }

    [Fact]
    public void UniquenessTokenChangesWhenTheShopperResubscribes()
    {
        Assert.NotEqual(
            _factory.UniquenessToken("subscription", "cust", "eshop-pro", "0"),
            _factory.UniquenessToken("subscription", "cust", "eshop-pro", "1"));
    }

    [Fact]
    public void UniquenessTokensAreScopedPerOperation()
    {
        Assert.NotEqual(
            _factory.UniquenessToken("customer", "cust"),
            _factory.UniquenessToken("subscription", "cust"));
    }

    [Fact]
    public void UniquenessTokenIsLongAndOpaqueAsMaxioExpects()
    {
        var token = _factory.UniquenessToken("subscription", "cust", "eshop-pro", "0");

        Assert.True(token.Length >= 32);
        Assert.DoesNotContain("cust", token);
        Assert.All(token, character => Assert.True(char.IsLetterOrDigit(character) || character == '-'));
    }
}
