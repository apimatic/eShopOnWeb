using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "api-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void ValidOptionsReportNoProblems()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void ApiKeyIsRequired()
    {
        var options = Valid();
        options.ApiKey = null;

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void ProductFamilyHandleIsRequired()
    {
        var options = Valid();
        options.ProductFamilyHandle = "";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void SubdomainIsRequiredWithoutBaseUrl()
    {
        var options = Valid();
        options.Subdomain = null;

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void BaseUrlReplacesTheSubdomainRequirement()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://billing.internal.example.com";

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void BaseUrlMustBeAbsolute()
    {
        var options = Valid();
        options.BaseUrl = "/not-absolute";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void UnknownEnvironmentIsRejected()
    {
        var options = Valid();
        options.Environment = "MARS";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:Environment"));
    }

    [Fact]
    public void UnknownCollectionMethodIsRejected()
    {
        var options = Valid();
        options.PaymentCollectionMethod = "cash";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Fact]
    public void UsSubdomainIsTemplatedIntoTheSpecServerUrl()
    {
        var options = Valid();

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void EuSubdomainIsTemplatedIntoTheEuServerUrl()
    {
        var options = Valid();
        options.Environment = MaxioEnvironments.Eu;

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var options = Valid();
        options.BaseUrl = "https://billing.internal.example.com/maxio";

        Assert.Equal("https://billing.internal.example.com/maxio/", options.ResolveBaseAddress().ToString());
    }
}
