using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioCustomerReferenceTests
{
    [Fact]
    public void ReturnsTheSameReferenceForTheSameUserEveryTime()
    {
        // The reference is the only link between an eShopOnWeb login and its Maxio customer. It is
        // recomputed on every request rather than stored, so this stability is what makes the integration
        // survive a restart on the in-memory database.
        Assert.Equal(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For("demouser@microsoft.com"));
    }

    [Fact]
    public void IgnoresCasingAndSurroundingWhitespaceInTheUserName()
    {
        Assert.Equal(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For("  DemoUser@Microsoft.COM "));
    }

    [Fact]
    public void ReturnsADifferentReferenceForADifferentUser()
    {
        Assert.NotEqual(
            MaxioCustomerReference.For("demouser@microsoft.com"),
            MaxioCustomerReference.For("admin@microsoft.com"));
    }

    [Fact]
    public void DoesNotLeakTheUserNameIntoTheThirdPartyIdentifier()
    {
        var reference = MaxioCustomerReference.For("demouser@microsoft.com");

        Assert.StartsWith("eshoponweb-", reference);
        Assert.DoesNotContain("demouser", reference);
        Assert.DoesNotContain("@", reference);
    }

    [Fact]
    public void RejectsAnEmptyUserName()
    {
        Assert.Throws<ArgumentException>(() => MaxioCustomerReference.For("  "));
    }
}
