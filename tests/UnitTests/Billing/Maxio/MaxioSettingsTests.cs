using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void Base_address_is_derived_from_the_subdomain_when_no_override_is_given()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "f" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void Base_address_override_is_used_verbatim()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "ignored",
            ProductFamilyHandle = "f",
            BaseUrl = "https://acme.ebilling.maxio.com/"
        };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void Base_address_override_without_a_trailing_slash_still_composes_with_relative_paths()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://proxy.internal/maxio" };

        Assert.Equal(new Uri("https://proxy.internal/maxio/subscriptions.json"),
            new Uri(settings.ResolveBaseAddress(), "subscriptions.json"));
    }

    [Fact]
    public void An_override_makes_the_subdomain_optional()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://proxy.internal/" };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void Missing_settings_are_named_so_the_operator_can_act_on_them()
    {
        var missing = new MaxioSettings().DescribeMissingSettings();

        Assert.Equal(new[] { "Maxio:ApiKey", "Maxio:Subdomain", "Maxio:ProductFamilyHandle" }, missing);
    }

    [Fact]
    public void A_malformed_override_is_rejected()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "not-a-url" };

        Assert.Throws<UriFormatException>(() => settings.ResolveBaseAddress());
    }
}
