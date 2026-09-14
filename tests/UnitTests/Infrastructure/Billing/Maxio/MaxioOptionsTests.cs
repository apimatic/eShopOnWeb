using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesTheHostFromTheSubdomain()
    {
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "cp-exp-3", ProductFamilyHandle = "f" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseAddress_PrefersAConfiguredBaseUrl()
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            Subdomain = "cp-exp-3",
            ProductFamilyHandle = "f",
            BaseUrl = "https://billing.internal.example.com"
        };

        Assert.Equal("https://billing.internal.example.com/", options.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void Validate_RequiresAnApiKey()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3", ProductFamilyHandle = "f" };

        Assert.Throws<ValidationException>(options.Validate);
    }

    [Fact]
    public void Validate_RequiresAProductFamilyHandle()
    {
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "cp-exp-3" };

        Assert.Throws<ValidationException>(options.Validate);
    }

    [Fact]
    public void Validate_RequiresASubdomainUnlessABaseUrlIsGiven()
    {
        var withoutEither = new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "f" };
        Assert.Throws<ValidationException>(withoutEither.Validate);

        var withBaseUrl = new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "f",
            BaseUrl = "https://billing.internal.example.com"
        };
        withBaseUrl.Validate();
    }

    [Fact]
    public void Validate_RejectsARelativeBaseUrl()
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            Subdomain = "cp-exp-3",
            ProductFamilyHandle = "f",
            BaseUrl = "/maxio"
        };

        Assert.Throws<ValidationException>(options.Validate);
    }
}
