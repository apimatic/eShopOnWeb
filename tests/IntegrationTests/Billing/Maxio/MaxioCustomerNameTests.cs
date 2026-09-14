#nullable enable
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioCustomerNameTests
{
    [Fact]
    public void SplitsAMultiPartLocalPartIntoFirstAndLastName()
    {
        var (first, last, organization) = MaxioCustomerName.Derive(new SubscriberIdentity("ann.lee@contoso.com"));

        Assert.Equal("Ann", first);
        Assert.Equal("Lee", last);
        Assert.Equal("contoso.com", organization);
    }

    [Fact]
    public void FallsBackToTheDomainWhenTheLocalPartIsASingleToken()
    {
        var (first, last, _) = MaxioCustomerName.Derive(new SubscriberIdentity("demouser@microsoft.com"));

        Assert.Equal("Demouser", first);
        Assert.Equal("Microsoft", last);
    }

    [Fact]
    public void PrefersNamesSuppliedByTheCaller()
    {
        var (first, last, organization) = MaxioCustomerName.Derive(
            new SubscriberIdentity("demouser@microsoft.com", firstName: "Dana", lastName: "Ortiz",
                organization: "Northwind"));

        Assert.Equal("Dana", first);
        Assert.Equal("Ortiz", last);
        Assert.Equal("Northwind", organization);
    }

    [Fact]
    public void NeverProducesABlankLastNameWhichMaxioWouldReject()
    {
        var (first, last, _) = MaxioCustomerName.Derive(new SubscriberIdentity("shopper"));

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(last));
    }
}
