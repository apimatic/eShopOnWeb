using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void DerivesBaseAddressFromSubdomainUsingTheSpecServerTemplate()
    {
        var settings = Valid();

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSupplied()
    {
        var settings = Valid();
        settings.BaseUrl = "https://maxio.internal.test/gateway/";

        Assert.Equal("https://maxio.internal.test/gateway/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AppendsTrailingSlashSoRelativePathsResolveUnderTheBasePath()
    {
        var settings = Valid();
        settings.BaseUrl = "https://maxio.internal.test/gateway";

        var resolved = new System.Uri(settings.ResolveBaseAddress(), "customers.json");

        Assert.Equal("https://maxio.internal.test/gateway/customers.json", resolved.ToString());
    }

    [Fact]
    public void BaseUrlOverrideRemovesTheNeedForASubdomain()
    {
        var settings = Valid();
        settings.Subdomain = null;
        settings.BaseUrl = "https://maxio.internal.test";

        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void ReportsEveryMissingKeyAtOnce()
    {
        var problems = new MaxioSettings().Validate();

        Assert.Contains(problems, p => p.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, p => p.Contains("Maxio:Subdomain"));
        Assert.Contains(problems, p => p.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void RejectsARelativeBaseUrl()
    {
        var settings = Valid();
        settings.BaseUrl = "/not-absolute";

        Assert.Contains(settings.Validate(), p => p.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void DefaultsToRemittanceSoSubscribeWorksWithoutAStoredCard()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
