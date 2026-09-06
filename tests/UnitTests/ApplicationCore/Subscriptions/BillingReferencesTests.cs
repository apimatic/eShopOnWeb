using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class BillingReferencesTests
{
    [Fact]
    public void IsStableForTheSameUser()
    {
        Assert.Equal(
            BillingReferences.ForUser("demouser@microsoft.com"),
            BillingReferences.ForUser("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void IgnoresCasingAndSurroundingWhitespace(string userName)
    {
        Assert.Equal(BillingReferences.ForUser("demouser@microsoft.com"), BillingReferences.ForUser(userName));
    }

    [Fact]
    public void DistinguishesDifferentUsers()
    {
        Assert.NotEqual(BillingReferences.ForUser("a@example.com"), BillingReferences.ForUser("b@example.com"));
    }

    [Fact]
    public void IsNamespacedSoSeveralApplicationsCanShareABillingSite()
    {
        Assert.StartsWith(BillingReferences.Prefix, BillingReferences.ForUser("demouser@microsoft.com"));
    }
}
