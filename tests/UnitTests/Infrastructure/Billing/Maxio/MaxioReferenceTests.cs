using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferenceTests
{
    [Fact]
    public void CustomerReferenceIsStableForTheSameUser()
    {
        Assert.Equal(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void CustomerReferenceIgnoresCasingAndSurroundingSpace(string variant)
    {
        Assert.Equal(MaxioReference.ForCustomer("demouser@microsoft.com"), MaxioReference.ForCustomer(variant));
    }

    [Fact]
    public void CustomerReferenceIsDifferentForDifferentUsers()
    {
        Assert.NotEqual(
            MaxioReference.ForCustomer("demouser@microsoft.com"),
            MaxioReference.ForCustomer("admin@microsoft.com"));
    }

    [Fact]
    public void CustomerReferenceSeparatesUsersThatSlugIdentically()
    {
        // Slugging alone would map both of these onto "a-b-com"; the hash is what keeps them apart.
        Assert.NotEqual(MaxioReference.ForCustomer("a@b.com"), MaxioReference.ForCustomer("a-b.com"));
    }

    [Fact]
    public void CustomerReferenceStaysReadable()
    {
        Assert.StartsWith("eshop-demouser-microsoft-com-", MaxioReference.ForCustomer("demouser@microsoft.com"));
    }

    [Fact]
    public void SubscriptionReferenceIsStableForTheSameCustomerAndKey()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");

        Assert.Equal(
            MaxioReference.ForSubscription(customer, "eshop-pro"),
            MaxioReference.ForSubscription(customer, "eshop-pro"));
    }

    [Fact]
    public void SubscriptionReferenceDiffersPerPlan()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");

        Assert.NotEqual(
            MaxioReference.ForSubscription(customer, "eshop-pro"),
            MaxioReference.ForSubscription(customer, "basic-plan"));
    }

    [Fact]
    public void SubscriptionReferenceDiffersPerCustomer()
    {
        Assert.NotEqual(
            MaxioReference.ForSubscription(MaxioReference.ForCustomer("a@example.com"), "eshop-pro"),
            MaxioReference.ForSubscription(MaxioReference.ForCustomer("b@example.com"), "eshop-pro"));
    }

    [Fact]
    public void OpaqueIdempotencyKeysStayDistinctEvenWhenTheySlugAlike()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");

        var first = MaxioReference.ForSubscription(customer, "9f8a1c22-0000-4a1b-9c3d-111111111111");
        var second = MaxioReference.ForSubscription(customer, "9f8a1c22-0000-4a1b-9c3d-222222222222");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ResubscribeReferenceIsDerivedFromTheSubscriptionItReplaces()
    {
        var customer = MaxioReference.ForCustomer("demouser@microsoft.com");
        var subscription = MaxioReference.ForSubscription(customer, "eshop-pro");

        Assert.Equal($"{subscription}-r42", MaxioReference.ForResubscribe(subscription, 42));
    }
}
