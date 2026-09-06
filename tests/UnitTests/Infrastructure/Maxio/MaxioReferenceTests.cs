using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferenceTests
{
    [Fact]
    public void CustomerReferenceIsDeterministicAndSlugged()
    {
        var reference = MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com");

        Assert.Equal("eshoponweb-demouser-microsoft-com", reference);
        Assert.Equal(reference, MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com"));
    }

    [Fact]
    public void CustomerReferenceIgnoresCasingAndSurroundingWhitespace()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com"),
            MaxioReference.ForCustomer("eshoponweb", "  DemoUser@Microsoft.COM  "));
    }

    [Fact]
    public void DifferentUsersGetDifferentCustomerReferences()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("eshoponweb", "a@example.com"),
            MaxioReference.ForCustomer("eshoponweb", "b@example.com"));
    }

    [Fact]
    public void SubscriptionReferenceCombinesCustomerAndPlan()
    {
        var customer = MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com");

        Assert.Equal("eshoponweb-demouser-microsoft-com-eshop-pro",
            MaxioReference.ForSubscription(customer, "eshop-pro"));
    }

    [Fact]
    public void SubscriptionReferenceVariesByAttempt()
    {
        var customer = MaxioReference.ForCustomer("eshoponweb", "demouser@microsoft.com");

        Assert.Equal("eshoponweb-demouser-microsoft-com-eshop-pro-2",
            MaxioReference.ForSubscription(customer, "eshop-pro", attempt: 2));
        Assert.Equal("eshoponweb-demouser-microsoft-com-eshop-pro-3",
            MaxioReference.ForSubscription(customer, "eshop-pro", attempt: 3));
    }

    [Fact]
    public void LongIdentitiesAreTruncatedAndDisambiguatedByHash()
    {
        var longUser = new string('a', 300) + "@example.com";

        var first = MaxioReference.ForCustomer("eshoponweb", longUser);
        var second = MaxioReference.ForCustomer("eshoponweb", new string('b', 300) + "@example.com");

        Assert.True(first.Length <= 100);
        Assert.Equal(first, MaxioReference.ForCustomer("eshoponweb", longUser));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void RejectsEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => MaxioReference.ForCustomer("eshoponweb", "  "));
        Assert.Throws<ArgumentException>(() => MaxioReference.ForSubscription("customer", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaxioReference.ForSubscription("customer", "plan", attempt: 0));
    }
}
