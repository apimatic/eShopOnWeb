using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsResolveBaseAddress
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme", Environment = MaxioOptions.UsEnvironment };

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DerivesTheEuHostFromTheSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme", Environment = MaxioOptions.EuEnvironment };

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "acme",
            Environment = MaxioOptions.EuEnvironment,
            BaseUrl = "https://billing.internal.example/api/"
        };

        Assert.Equal("https://billing.internal.example/api/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AppendsTheTrailingSlashRelativePathsNeed()
    {
        var options = new MaxioOptions { BaseUrl = "https://billing.internal.example/api" };

        Assert.Equal("https://billing.internal.example/api/", options.ResolveBaseAddress().ToString());
    }
}

public class MaxioOptionsValidatorValidate
{
    private readonly MaxioOptionsValidator _validator = new();

    [Fact]
    public void SucceedsForACompleteConfiguration()
    {
        Assert.True(_validator.Validate(null, MaxioTestOptions.Valid()).Succeeded);
    }

    [Fact]
    public void ReportsEveryMissingSettingByName()
    {
        var result = _validator.Validate(null, new MaxioOptions());

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
        Assert.Contains("Maxio:Subdomain", result.FailureMessage);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    [Fact]
    public void AcceptsAMissingSubdomainWhenBaseUrlIsSet()
    {
        var options = MaxioTestOptions.Valid();
        options.Subdomain = string.Empty;
        options.BaseUrl = "https://billing.internal.example";

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void RejectsARelativeBaseUrl()
    {
        var options = MaxioTestOptions.Valid();
        options.BaseUrl = "/not-absolute";

        Assert.Contains("must be an absolute URL", _validator.Validate(null, options).FailureMessage);
    }

    [Fact]
    public void RejectsACollectionMethodTheSpecificationDoesNotDefine()
    {
        var options = MaxioTestOptions.Valid();
        options.PaymentCollectionMethod = "cheque";

        Assert.Contains("PaymentCollectionMethod", _validator.Validate(null, options).FailureMessage);
    }
}
