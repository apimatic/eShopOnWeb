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

        Assert.True(MaxioReferenceKeys.TryParseSubscriptionReference(
            reference, out var plan, out var generation));
        Assert.Equal("eshop-pro", plan);
        Assert.Equal(1, generation);
    }

    [Fact]
    public void SubscriptionReferenceGenerationAppendsSuffixAndParsesBack()
    {
        var first = MaxioReferenceKeys.SubscriptionReference("demouser@microsoft.com", "basic-plan", 1);
        var second = MaxioReferenceKeys.SubscriptionReference("demouser@microsoft.com", "basic-plan", 2);
        var third = MaxioReferenceKeys.SubscriptionReference("demouser@microsoft.com", "basic-plan", 3);

        Assert.Equal("eshop-sub-basic-plan-" + MaxioReferenceKeys.SubscriptionReference("demouser@microsoft.com", "basic-plan", 1).Substring("eshop-sub-basic-plan-".Length), first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.EndsWith("-g2", second, StringComparison.Ordinal);
        Assert.EndsWith("-g3", third, StringComparison.Ordinal);

        Assert.True(MaxioReferenceKeys.TryParseSubscriptionReference(second, out var plan2, out var gen2));
        Assert.Equal("basic-plan", plan2);
        Assert.Equal(2, gen2);

        Assert.True(MaxioReferenceKeys.TryParseSubscriptionReference(third, out var plan3, out var gen3));
        Assert.Equal("basic-plan", plan3);
        Assert.Equal(3, gen3);
    }

    [Fact]
    public void SubscriptionReferenceIsDeterministicPerGeneration()
    {
        Assert.Equal(
            MaxioReferenceKeys.SubscriptionReference("a@example.com", "eshop-pro", 2),
            MaxioReferenceKeys.SubscriptionReference("a@example.com", "eshop-pro", 2));
    }

    [Fact]
    public void PlanHandleParseRejectsForeignReferences()
    {
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle(null));
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle("some-other-reference"));
        Assert.Null(MaxioReferenceKeys.TryGetPlanHandle("eshop-sub-eshop-pro-nothex"));
        Assert.False(MaxioReferenceKeys.TryParseSubscriptionReference(
            "eshop-sub-eshop-pro-nothex", out _, out _));
    }
}
