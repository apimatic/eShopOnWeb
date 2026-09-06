using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferencesTests
{
    [Fact]
    public void CustomerReferenceIsNamespacedAndCaseInsensitive()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com",
            MaxioReferences.ForCustomer("eshoponweb", "DemoUser@Microsoft.com"));
    }

    [Fact]
    public void FirstSubscriptionReferenceHasNoOrdinal()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com:eshop-pro",
            MaxioReferences.ForSubscription("eshoponweb:demouser@microsoft.com", "eshop-pro", attempt: 1));
    }

    [Fact]
    public void LaterSubscriptionReferencesAreOrdinalSuffixed()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com:eshop-pro:3",
            MaxioReferences.ForSubscription("eshoponweb:demouser@microsoft.com", "eshop-pro", attempt: 3));
    }

    [Fact]
    public void KeyedReferenceIgnoresThePlanSoAReplayCannotForkIntoASecondSubscription()
    {
        var forPro = MaxioReferences.ForSubscription("eshoponweb:demouser@microsoft.com", "checkout-42");
        var forBasic = MaxioReferences.ForSubscription("eshoponweb:demouser@microsoft.com", "checkout-42");

        Assert.Equal("eshoponweb:demouser@microsoft.com:key:checkout-42", forPro);
        Assert.Equal(forPro, forBasic);
    }

    [Fact]
    public void OverlongIdempotencyKeysAreTruncated()
    {
        var key = new string('k', MaxioReferences.MaxIdempotencyKeyLength + 50);

        var reference = MaxioReferences.ForSubscription("customer", key);

        Assert.Equal($"customer:key:{new string('k', MaxioReferences.MaxIdempotencyKeyLength)}", reference);
    }
}
