using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioReferenceKeysTests
{
    [Theory]
    [InlineData("demouser@microsoft.com")]
    [InlineData("admin@microsoft.com")]
    [InlineData("someone@example.org")]
    public void CustomerReferenceIsDeterministicAndStable(string userName)
    {
        var first = MaxioReferenceKeys.CustomerReference(userName);
        var second = MaxioReferenceKeys.CustomerReference(userName);

        Assert.Equal(first, second);
        Assert.StartsWith("eshop-user-", first, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerReferencesDifferAcrossUsers()
    {
        Assert.NotEqual(
            MaxioReferenceKeys.CustomerReference("a@example.com"),
            MaxioReferenceKeys.CustomerReference("b@example.com"));
    }

    [Fact]
    public void SubscriptionReferenceEmbedsPlanHandleAndRoundTrips()
    {
        var reference = MaxioReferenceKeys.SubscriptionReference("demouser@microsoft.com", "eshop-pro");

        Assert.StartsWith("eshop-sub-eshop-pro-", reference, StringComparison.Ordinal);
        Assert.Equal("eshop-pro", MaxioReferenceKeys.TryGetPlanHandle(reference));
    }

    [Fact]
    public void PlanHandleParseRejectsForeignReferences()
    {
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle(null));
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle("some-other-reference"));
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle("eshop-sub-eshop-pro-nothex"));
    }
}
