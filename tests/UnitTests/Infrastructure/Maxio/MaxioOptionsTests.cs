using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomainServerTemplate()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        Assert.Equal(new Uri("https://cp-exp-1.chargify.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void UsesAnExplicitBaseUrlVerbatim()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://acme.ebilling.maxio.com"
        };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void RequiresASubdomainOrABaseUrl()
    {
        var options = new MaxioOptions();

        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseAddress());
    }

    [Fact]
    public void ValidationPassesForACompleteConfiguration()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions
        {
            ApiKey = "not-a-real-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = "eshop-subscribe"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationNamesEveryMissingSetting()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ApiKey"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:Subdomain"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ProductFamilyHandle"));
    }
}
