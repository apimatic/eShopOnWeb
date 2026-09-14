using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions ValidOptions() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void DerivesBaseUrlFromSubdomainUsingTheSpecificationServerTemplate()
    {
        var options = ValidOptions();

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesBaseUrlFromTheEuropeanTemplateWhenTheEnvironmentIsEu()
    {
        var options = ValidOptions();
        options.Environment = "eu";

        Assert.Equal("https://acme.ebilling.maxio.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenItIsSet()
    {
        var options = ValidOptions();
        options.BaseUrl = "https://billing.example.test";

        Assert.Equal("https://billing.example.test", options.ResolveBaseUrl());
    }

    [Fact]
    public void UsesBaseUrlWithoutASubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://billing.example.test/"
        };

        Assert.Empty(options.Validate());
        Assert.Equal("https://billing.example.test", options.ResolveBaseUrl());
    }

    [Fact]
    public void ValidOptionsReportNoProblems()
    {
        Assert.Empty(ValidOptions().Validate());
        Assert.True(ValidOptions().IsConfigured);
    }

    [Fact]
    public void ReportsEveryMissingSetting()
    {
        var problems = new MaxioOptions().Validate();

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, problem => problem.Contains("Maxio:Subdomain"));
        Assert.Contains(problems, problem => problem.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void ReportsABaseUrlThatIsNotAnAbsoluteUrl()
    {
        var options = ValidOptions();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), problem => problem.Contains("absolute URL"));
    }

    [Theory]
    [InlineData("eshop-subscribe", "handle:eshop-subscribe")]
    [InlineData("handle:eshop-subscribe", "handle:eshop-subscribe")]
    [InlineData("3023074", "3023074")]
    public void FormatsTheProductFamilyPathValueAsTheSpecificationRequires(string configured, string expected)
    {
        var options = ValidOptions();
        options.ProductFamilyHandle = configured;

        Assert.Equal(expected, options.ResolveProductFamilyPathValue());
    }
}
