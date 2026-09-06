using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// The customer reference is what makes enrolment idempotent — Maxio allows one customer per reference
/// value — so these assert the properties that guarantee actually hold.
/// </summary>
public class MaxioCustomerReferenceTests
{
    [Fact]
    public void IsStableForTheSameUser()
    {
        var first = MaxioCustomerReference.For("eshoponweb", "demouser@microsoft.com");
        var second = MaxioCustomerReference.For("eshoponweb", "demouser@microsoft.com");

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void IgnoresCasingAndSurroundingWhitespace(string variant)
    {
        var canonical = MaxioCustomerReference.For("eshoponweb", "demouser@microsoft.com");

        Assert.Equal(canonical, MaxioCustomerReference.For("eshoponweb", variant));
    }

    [Fact]
    public void DiffersBetweenUsers()
    {
        var demo = MaxioCustomerReference.For("eshoponweb", "demouser@microsoft.com");
        var admin = MaxioCustomerReference.For("eshoponweb", "admin@microsoft.com");

        Assert.NotEqual(demo, admin);
    }

    [Fact]
    public void DiffersBetweenUsersThatSanitizeToTheSameReadableText()
    {
        // Sanitizing "a.b@x.com" and "a-b@x.com" collapses both to the same readable text. The appended
        // fingerprint is what stops two people sharing one billing customer.
        var dotted = MaxioCustomerReference.For("eshoponweb", "a.b@x.com");
        var hyphenated = MaxioCustomerReference.For("eshoponweb", "a-b@x.com");

        Assert.NotEqual(dotted, hyphenated);
    }

    [Fact]
    public void DiffersBetweenApplicationsSharingOneMaxioSite()
    {
        var mine = MaxioCustomerReference.For("eshoponweb", "demouser@microsoft.com");
        var theirs = MaxioCustomerReference.For("other-app", "demouser@microsoft.com");

        Assert.NotEqual(mine, theirs);
    }

    [Fact]
    public void ContainsOnlyUrlSafeCharacters()
    {
        // It travels as a query-string value on the lookup call.
        var reference = MaxioCustomerReference.For("eshoponweb", "Some.User+tag@Example.co.uk");

        Assert.Matches("^[a-z0-9-]+$", reference);
    }
}
