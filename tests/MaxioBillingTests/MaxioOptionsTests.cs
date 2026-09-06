using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class MaxioOptionsTests
{
    [Fact]
    public void Derives_the_base_address_from_the_subdomain()
    {
        var options = Configured();

        Assert.Equal("https://cp-exp-1.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Uses_an_explicit_base_url_instead_of_deriving_one()
    {
        var options = Configured();
        options.BaseUrl = "https://billing.internal.example/maxio/";

        Assert.Equal("https://billing.internal.example/maxio/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void An_explicit_base_url_wins_over_the_subdomain()
    {
        var options = Configured();
        options.Subdomain = "ignored";
        options.BaseUrl = "https://billing.internal.example";

        Assert.Equal("https://billing.internal.example/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void An_explicit_base_url_keeps_its_path_so_relative_requests_land_underneath_it()
    {
        var options = Configured();
        options.BaseUrl = "https://gateway.example/maxio";

        var address = options.ResolveBaseAddress();

        Assert.Equal("/maxio/customers.json", new Uri(address, "customers.json").AbsolutePath);
    }

    [Fact]
    public void An_eu_site_resolves_to_the_eu_server_from_the_specification()
    {
        var options = Configured();
        options.Environment = "EU";

        Assert.Equal("https://cp-exp-1.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void A_subdomain_is_not_needed_when_a_base_url_is_given()
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "family",
            BaseUrl = "https://billing.example"
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData(null, "cp-exp-1", "family", "ApiKey")]
    [InlineData("k", null, "family", "Subdomain")]
    [InlineData("k", "cp-exp-1", null, "ProductFamilyHandle")]
    public void Names_the_setting_that_is_missing(string? apiKey, string? subdomain, string? family, string expected)
    {
        var options = new MaxioOptions
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = family
        };

        Assert.Contains(options.Validate(), problem => problem.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_collection_method_the_specification_does_not_define()
    {
        var options = Configured();
        options.PaymentCollectionMethod = "carrier-pigeon";

        Assert.Contains(options.Validate(), problem => problem.Contains("PaymentCollectionMethod", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_a_base_url_that_is_not_absolute()
    {
        var options = Configured();
        options.BaseUrl = "/relative/path";

        Assert.Contains(options.Validate(), problem => problem.Contains("BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Refuses_to_resolve_an_address_while_settings_are_missing()
    {
        var options = new MaxioOptions();

        var exception = Assert.Throws<BillingConfigurationException>(() => options.ResolveBaseAddress());

        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_to_a_collection_method_that_needs_no_stored_card()
    {
        Assert.Equal("remittance", new MaxioOptions().PaymentCollectionMethod);
    }

    private static MaxioOptions Configured() => new()
    {
        ApiKey = "k",
        Subdomain = "cp-exp-1",
        ProductFamilyHandle = "eshop-subscribe"
    };
}
