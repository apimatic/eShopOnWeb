using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void Us_sites_resolve_to_the_chargify_host_for_the_subdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Eu_sites_resolve_to_the_eu_hosted_billing_host()
    {
        var settings = new MaxioSettings { Subdomain = "acme", Environment = "EU" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData("https://billing.example.test/maxio/")]
    [InlineData("https://billing.example.test/maxio")]
    public void An_explicit_base_url_wins_over_the_subdomain(string configured)
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = configured };

        // A trailing slash is added when missing, otherwise the last path segment would be lost
        // when relative request paths are combined against it.
        Assert.Equal("https://billing.example.test/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Configuration_is_rejected_when_the_api_key_is_missing()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void Configuration_is_rejected_when_neither_a_subdomain_nor_a_base_url_is_supplied()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void A_base_url_stands_in_for_the_subdomain()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://billing.example.test/",
            ProductFamilyHandle = "eshop-subscribe",
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Configuration_is_rejected_when_the_product_family_is_missing()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ProductFamilyHandle"));
    }
}
