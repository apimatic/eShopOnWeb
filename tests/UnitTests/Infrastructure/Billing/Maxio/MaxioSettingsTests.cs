using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "family" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenItIsSet()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            BaseUrl = "https://acme.ebilling.maxio.com/"
        };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void KeepsBaseUrlPathsIntactByEnsuringATrailingSlash()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "family",
            BaseUrl = "https://proxy.internal/maxio"
        };

        Assert.Equal("https://proxy.internal/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void BaseUrlAloneIsEnoughToBeConfigured()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "family",
            BaseUrl = "https://proxy.internal/"
        };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void ReportsEveryMissingKeyAtOnce()
    {
        var problems = new MaxioSettings().Validate();

        Assert.False(new MaxioSettings().IsConfigured);
        Assert.Contains(problems, problem => problem.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, problem => problem.Contains("Maxio:Subdomain"));
        Assert.Contains(problems, problem => problem.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            BaseUrl = "not-a-uri"
        };

        Assert.Contains(settings.Validate(), problem => problem.Contains("absolute URI"));
    }

    [Fact]
    public void AFullyBoundSectionHasNoProblems()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "family"
        };

        Assert.Empty(settings.Validate().ToList());
    }
}
