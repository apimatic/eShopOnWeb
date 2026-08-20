using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioReferenceGeneratorTests
{
    [Fact]
    public void GeneratesStableDistinctReferences()
    {
        var customerReference = MaxioReferenceGenerator.CustomerReference("user-123");
        var sameCustomerReference = MaxioReferenceGenerator.CustomerReference("user-123");
        var firstSubscription = MaxioReferenceGenerator.SubscriptionReference("user-123", "basic-plan");
        var secondSubscription = MaxioReferenceGenerator.SubscriptionReference("user-123", "eshop-pro");

        Assert.Equal(customerReference, sameCustomerReference);
        Assert.StartsWith("eshop-c-", customerReference);
        Assert.StartsWith("eshop-s-", firstSubscription);
        Assert.NotEqual(firstSubscription, secondSubscription);
    }
}
