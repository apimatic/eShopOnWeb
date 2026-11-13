using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioConfigurationTests
{
    [Theory]
    [InlineData("Maxio:ApiKey")]
    [InlineData("Maxio:ProductFamilyHandle")]
    [InlineData("Maxio:Subdomain")]
    public void AMissingOrBlankSettingFailsValidation_NamingTheKey(string key)
    {
        using var host = new BillingTestHost(new Dictionary<string, string?> { [key] = "   " });

        var ex = Assert.Throws<OptionsValidationException>(() => host.Provider.GetRequiredService<IOptions<MaxioSettings>>().Value);

        Assert.Contains(ex.Failures, f => f.Contains(key));
    }

    [Fact]
    public void ValidationMessagesNeverEchoTheApiKey()
    {
        using var host = new BillingTestHost(new Dictionary<string, string?> { ["Maxio:ProductFamilyHandle"] = "" });

        var ex = Assert.Throws<OptionsValidationException>(() => host.Provider.GetRequiredService<IOptions<MaxioSettings>>().Value);

        Assert.DoesNotContain(ex.Failures, f => f.Contains("unit-test-placeholder-key"));
    }

    [Fact]
    public void SubdomainIsOptionalWhenABaseUrlOverrideIsSet()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://proxy.example.test"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ARelativeBaseUrlIsRejected()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings
        {
            ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "proxy.example.test"
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ReferencesAreDeterministicCaseInsensitiveAndContainNoUserName()
    {
        var a = MaxioReferences.ForCustomer("DemoUser@Microsoft.com");
        var b = MaxioReferences.ForCustomer(" demouser@microsoft.com ");

        Assert.Equal(a, b);
        Assert.DoesNotContain("demouser", a, StringComparison.OrdinalIgnoreCase);
        Assert.True(a.Length <= 64);
        Assert.NotEqual(MaxioReferences.ForSubscription(a, "pro"), MaxioReferences.ForSubscription(a, "basic"));
        Assert.Equal(MaxioReferences.ForSubscription(a, "pro"), MaxioReferences.ForSubscription(b, "pro"));
    }
}
