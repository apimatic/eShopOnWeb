using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class SubscriberIdentityTests
{
    [Fact]
    public void BillingReference_IsNamespacedAndLowerCased()
    {
        var identity = new SubscriberIdentity("DemoUser@Microsoft.com", "DemoUser@Microsoft.com");

        Assert.Equal("eshoponweb:demouser@microsoft.com", identity.BillingReference);
    }

    [Fact]
    public void BillingReference_IsStableAcrossCasingAndPadding()
    {
        var first = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com");
        var second = new SubscriberIdentity("demouser@microsoft.com", "  DEMOUSER@MICROSOFT.COM ");

        Assert.Equal(first.BillingReference, second.BillingReference);
    }

    [Fact]
    public void Names_AreDerivedFromTheEmailWhenNotSupplied()
    {
        var identity = new SubscriberIdentity("jane.doe@example.com", "jane.doe@example.com");

        Assert.Equal("Jane", identity.FirstName);
        Assert.Equal("Doe", identity.LastName);
    }

    [Fact]
    public void Names_FallBackToAPlaceholderSurnameForASingleTokenLocalPart()
    {
        var identity = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal("Demouser", identity.FirstName);
        Assert.Equal("Customer", identity.LastName);
    }

    [Fact]
    public void Names_PreferTheSuppliedValues()
    {
        var identity = new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com", " Ada ", " Lovelace ");

        Assert.Equal("Ada", identity.FirstName);
        Assert.Equal("Lovelace", identity.LastName);
    }
}
