using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void ResolvesUsServerTemplateFromSubdomain()
    {
        var options = Valid();

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolvesEuServerTemplateFromSubdomain()
    {
        var options = Valid();
        options.Environment = MaxioEnvironments.Eu;

        Assert.Equal("https://acme.ebilling.maxio.com", options.ResolveBaseAddress());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = Valid();
        options.BaseUrl = "https://billing.internal.example/api";

        Assert.Equal("https://billing.internal.example/api", options.ResolveBaseAddress());
    }

    [Fact]
    public void BaseUrlOverridesSubdomainAndEnvironment()
    {
        var options = Valid();
        options.Environment = MaxioEnvironments.Eu;
        options.BaseUrl = "https://localhost:9999";

        Assert.Equal("https://localhost:9999", options.ResolveBaseAddress());
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void SubdomainIsNotRequiredWhenBaseUrlIsSet()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://localhost:9999";

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void ReportsMissingApiKey()
    {
        var options = Valid();
        options.ApiKey = "  ";

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void ReportsMissingProductFamilyHandle()
    {
        var options = Valid();
        options.ProductFamilyHandle = null;

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void ReportsMissingSubdomainWhenNoBaseUrl()
    {
        var options = Valid();
        options.Subdomain = null;

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void ReportsUnknownEnvironment()
    {
        var options = Valid();
        options.Environment = "MARS";

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:Environment"));
    }

    [Fact]
    public void ReportsUnknownCollectionMethod()
    {
        var options = Valid();
        options.PaymentCollectionMethod = "cash";

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Fact]
    public void ReportsNonAbsoluteBaseUrl()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), e => e.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void DefaultsToRemittanceSoSubscribeWorksWithoutACard()
    {
        Assert.Equal(MaxioCollectionMethods.Remittance, new MaxioOptions().PaymentCollectionMethod);
    }
}
