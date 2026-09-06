using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferenceTests
{
    [Fact]
    public void CustomerReferenceIsNamespacedAndNormalised()
    {
        Assert.Equal(
            "eshop:demouser@microsoft.com",
            MaxioReference.ForCustomer("eshop", "  DemoUser@Microsoft.com "));
    }

    [Fact]
    public void FirstSubscriptionSlotHasNoSuffix()
    {
        Assert.Equal(
            "eshop:demouser@microsoft.com:eshop-pro",
            MaxioReference.ForSubscription("eshop:demouser@microsoft.com", "eshop-pro", attempt: 1));
    }

    [Fact]
    public void LaterSlotsAreSuffixedSoAReSubscribeGetsAFreshReference()
    {
        Assert.Equal(
            "eshop:demouser@microsoft.com:eshop-pro#3",
            MaxioReference.ForSubscription("eshop:demouser@microsoft.com", "eshop-pro", attempt: 3));
    }

    [Fact]
    public void AttemptsStartAtOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaxioReference.ForSubscription("eshop:someone", "eshop-pro", attempt: 0));
    }

    [Fact]
    public void ScopeIsThePlanHandleWhenNoIdempotencyKeyIsSupplied()
    {
        Assert.Equal("eshop-pro", MaxioReference.ScopeFor("eshop-pro", idempotencyKey: null));
        Assert.Equal("eshop-pro", MaxioReference.ScopeFor("eshop-pro", idempotencyKey: "   "));
    }

    [Fact]
    public void AnIdempotencyKeyReplacesThePlanHandleAsTheScope()
    {
        Assert.Equal("key:cart-7f3a", MaxioReference.ScopeFor("eshop-pro", "cart-7f3a"));
    }

    [Fact]
    public void DifferentPlansGetDifferentReferencesForTheSameShopper()
    {
        var customer = MaxioReference.ForCustomer("eshop", "demouser@microsoft.com");

        var pro = MaxioReference.ForSubscription(customer, MaxioReference.ScopeFor("eshop-pro", null), 1);
        var basic = MaxioReference.ForSubscription(customer, MaxioReference.ScopeFor("basic-plan", null), 1);

        Assert.NotEqual(pro, basic);
    }

    [Fact]
    public void OverlongReferencesAreTruncatedButStayUnique()
    {
        var first = MaxioReference.ForCustomer("eshop", new string('a', 300) + "1@example.com");
        var second = MaxioReference.ForCustomer("eshop", new string('a', 300) + "2@example.com");

        Assert.True(first.Length <= 255);
        Assert.True(second.Length <= 255);
        Assert.NotEqual(first, second);
    }
}
