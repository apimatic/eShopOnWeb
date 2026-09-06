using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioEnvironmentsTests
{
    [Fact]
    public void DerivesTheUsProductionServerFromTheSubdomain()
    {
        var settings = MaxioTestFactory.Settings(s => s.Subdomain = "acme");

        Assert.Equal("https://acme.chargify.com/", MaxioEnvironments.ResolveBaseAddress(settings).AbsoluteUri);
    }

    [Fact]
    public void DerivesTheEuProductionServerFromTheSubdomain()
    {
        var settings = MaxioTestFactory.Settings(s =>
        {
            s.Subdomain = "acme";
            s.Environment = MaxioEnvironments.Europe;
        });

        Assert.Equal("https://acme.ebilling.maxio.com/", MaxioEnvironments.ResolveBaseAddress(settings).AbsoluteUri);
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenItIsSet()
    {
        var settings = MaxioTestFactory.Settings(s =>
        {
            s.BaseUrl = "https://billing-gateway.internal:8443/maxio/";
            s.Subdomain = "ignored";
            s.Environment = MaxioEnvironments.Europe;
        });

        Assert.Equal("https://billing-gateway.internal:8443/maxio/", MaxioEnvironments.ResolveBaseAddress(settings).AbsoluteUri);
    }

    [Fact]
    public void AppendsTheTrailingSlashARelativeUriNeeds()
    {
        var settings = MaxioTestFactory.Settings(s => s.BaseUrl = "https://billing-gateway.internal/maxio");

        Assert.Equal("https://billing-gateway.internal/maxio/", MaxioEnvironments.ResolveBaseAddress(settings).AbsoluteUri);
    }

    [Fact]
    public void ReportsEveryMissingSettingByKeyName()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<SubscriptionBillingConfigurationException>(
            () => MaxioEnvironments.ResolveBaseAddress(settings));

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void RejectsAnUnknownEnvironment()
    {
        var settings = MaxioTestFactory.Settings(s => s.Environment = "APAC");

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:Environment"));
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void RejectsAnUnknownPaymentCollectionMethod()
    {
        var settings = MaxioTestFactory.Settings(s => s.PaymentCollectionMethod = "cheque");

        Assert.Contains(settings.Validate(), problem => problem.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Fact]
    public void TreatsAnEmptyPaymentCollectionMethodAsUnset()
    {
        var settings = MaxioTestFactory.Settings(s => s.PaymentCollectionMethod = "  ");

        Assert.True(settings.IsConfigured);
        Assert.Null(settings.EffectivePaymentCollectionMethod);
    }

    [Fact]
    public void SubdomainOnlyBecomesOptionalWhenBaseUrlIsSet()
    {
        var settings = MaxioTestFactory.Settings(s =>
        {
            s.Subdomain = null;
            s.BaseUrl = "https://gateway.example/";
        });

        Assert.True(settings.IsConfigured);
    }
}
