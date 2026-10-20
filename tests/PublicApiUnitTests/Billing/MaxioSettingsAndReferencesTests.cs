using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.PublicApiUnitTests.Billing;

public class MaxioSettingsAndReferencesTests
{
    [Theory]
    [InlineData(null, "site", "family", null, "Maxio:ApiKey")]
    [InlineData("   ", "site", "family", null, "Maxio:ApiKey")]
    [InlineData("key", "", "family", null, "Maxio:Subdomain")]
    [InlineData("key", "site", " ", null, "Maxio:ProductFamilyHandle")]
    [InlineData("key", "", "family", "not a url", "Maxio:BaseUrl")]
    public void ValidatorRefusesMissingOrBlankSettingsAndNamesTheKey(string? apiKey, string? subdomain, string? family, string? baseUrl, string expectedKey)
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family, BaseUrl = baseUrl,
        });

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);
    }

    [Fact]
    public void ValidatorAcceptsBaseUrlInsteadOfSubdomain()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "key", ProductFamilyHandle = "family", BaseUrl = "https://example.test",
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationFailureNeverEchoesTheConfiguredValue()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "secret-looking-value", ProductFamilyHandle = "", Subdomain = "",
        });

        Assert.DoesNotContain("secret-looking-value", result.FailureMessage);
    }

    [Fact]
    public void RegistrationBindsTheMaxioSectionAndFailsOnMissingKey()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:Subdomain"] = "site",
            ["Maxio:ProductFamilyHandle"] = "family",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);
        Assert.Contains("Maxio:ApiKey", ex.Message);
    }

    [Fact]
    public void ReferencesAreDeterministicCaseInsensitiveAndPiiFree()
    {
        var a = BillingReferences.ForCustomer("Demouser@Microsoft.com");
        var b = BillingReferences.ForCustomer(" demouser@microsoft.com ");

        Assert.Equal(a, b);
        Assert.StartsWith(BillingReferences.CustomerPrefix, a);
        Assert.DoesNotContain("demouser", a);
        Assert.True(a.Length <= 64);

        var pro = BillingReferences.ForSubscription(a, "eshop-pro");
        Assert.Equal(pro, BillingReferences.ForSubscription(a, "ESHOP-PRO"));
        Assert.NotEqual(pro, BillingReferences.ForSubscription(a, "basic-plan"));
        Assert.True(pro.Length <= 64);
    }

    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    [InlineData("123@example.com", "eShop", "Customer")]
    public void ShopperNamesAreDerivedFromTheEmailLocalPart(string email, string first, string last)
    {
        var profile = ShopperProfile.FromEmail(email);

        Assert.Equal(first, profile.FirstName);
        Assert.Equal(last, profile.LastName);
        Assert.Equal(email, profile.Email);
    }

    [Theory]
    [InlineData(29900L, "USD", "$299.00")]
    [InlineData(2900L, "EUR", "29.00 EUR")]
    [InlineData(1L, null, "0.01")]
    public void PricesAreFormattedFromCents(long cents, string? currency, string expected)
    {
        Assert.Equal(expected, PriceFormatter.Format(cents, currency));
    }
}
