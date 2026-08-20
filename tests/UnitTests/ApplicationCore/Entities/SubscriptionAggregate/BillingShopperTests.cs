using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.SubscriptionAggregate;

public class BillingShopperTests
{
    [Fact]
    public void FromIdentity_UsesUserIdAsNamespacedCustomerReference()
    {
        var shopper = BillingShopper.FromIdentity("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal("eshop:user-123", shopper.CustomerReference);
        Assert.Equal("demouser@microsoft.com", shopper.Email);
        Assert.Equal("Demouser", shopper.FirstName);
        Assert.Equal("eShopOnWeb", shopper.LastName);
    }

    [Fact]
    public void OpenSubscription_TreatsActiveAsOpenAndCanceledAsClosed()
    {
        var active = new CustomerSubscription(1, "active", "eshop-pro", "Pro Plan", 299m, null);
        var canceled = new CustomerSubscription(2, "canceled", "eshop-pro", "Pro Plan", 299m, null);

        Assert.True(active.IsOpen);
        Assert.False(canceled.IsOpen);
    }
}
