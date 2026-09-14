using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferenceTests
{
    private const string Prefix = "eshoponweb";

    [Fact]
    public void ForCustomer_IsStableAcrossCalls()
    {
        var first = MaxioReference.ForCustomer(Prefix, "DEMOUSER@MICROSOFT.COM");
        var second = MaxioReference.ForCustomer(Prefix, "DEMOUSER@MICROSOFT.COM");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForCustomer_IgnoresCasingAndSurroundingWhitespace()
    {
        var upper = MaxioReference.ForCustomer(Prefix, "  DemoUser@microsoft.com  ");
        var lower = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void ForCustomer_ProducesAReadableSlugOfTheUserKey()
    {
        var reference = MaxioReference.ForCustomer(Prefix, "demouser@microsoft.com");

        Assert.StartsWith("eshoponweb-demouser-microsoft-com-", reference);
    }

    [Fact]
    public void ForCustomer_DistinguishesUserKeysThatSlugAlike()
    {
        // Both slug to "a-b-example-com"; the digest is what keeps them apart.
        var dotted = MaxioReference.ForCustomer(Prefix, "a.b@example.com");
        var dashed = MaxioReference.ForCustomer(Prefix, "a-b@example.com");

        Assert.NotEqual(dotted, dashed);
    }

    [Fact]
    public void ForSubscription_CombinesCustomerReferenceAndPlan()
    {
        var reference = MaxioReference.ForSubscription("eshoponweb-demouser-abc123", "eshop-pro");

        Assert.Equal("eshoponweb-demouser-abc123--eshop-pro", reference);
    }

    [Fact]
    public void ForSubscription_SuffixesLaterAttempts()
    {
        var first = MaxioReference.ForSubscription("cust", "eshop-pro");
        var second = MaxioReference.ForSubscription("cust", "eshop-pro", attempt: 2);

        Assert.Equal("cust--eshop-pro", first);
        Assert.Equal("cust--eshop-pro--2", second);
    }
}
