using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "an-api-key",
        Subdomain = "a-site",
        ProductFamilyHandle = "a-family"
    };

    [Fact]
    public void DerivesTheApiHostFromTheSubdomain()
    {
        var settings = Valid();

        Assert.Equal("https://a-site.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesTheBaseUrlOverrideVerbatimWhenItIsSet()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.example.com/api/v1/billing";

        Assert.Equal("https://gateway.example.com/api/v1/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DoesNotDoubleTheTrailingSlashOnAnOverride()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.example.com/";

        Assert.Equal("https://gateway.example.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AcceptsSettingsThatAreCompleteEnoughToUse()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var settings = Valid();
        settings.ApiKey = "";

        Assert.Contains(settings.Validate(), p => p.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void RequiresAProductFamilyHandleBecauseItSelectsTheOfferedPlans()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = "";

        Assert.Contains(settings.Validate(), p => p.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void RequiresASubdomainOnlyWhenThereIsNoBaseUrl()
    {
        var settings = Valid();
        settings.Subdomain = "";

        Assert.Contains(settings.Validate(), p => p.Contains("Maxio:Subdomain"));

        settings.BaseUrl = "https://gateway.example.com";
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteUrl()
    {
        var settings = Valid();
        settings.BaseUrl = "not-a-url";

        Assert.Contains(settings.Validate(), p => p.Contains("absolute URL"));
    }

    [Fact]
    public void ReportsEveryProblemAtOnceRatherThanTheFirst()
    {
        var settings = new MaxioSettings();

        Assert.True(settings.Validate().Count() >= 3);
    }
}
