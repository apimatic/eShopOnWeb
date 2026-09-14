using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = "example-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomain()
    {
        Assert.Equal(new Uri("https://example-site.chargify.com/"), Valid().ResolveBaseAddress());
    }

    [Fact]
    public void PrefersAnExplicitBaseUrlOverTheSubdomain()
    {
        var settings = Valid();
        settings.BaseUrl = "https://example-site.ebilling.maxio.com/";

        Assert.Equal(new Uri("https://example-site.ebilling.maxio.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void KeepsTheConfiguredPathOfAnExplicitBaseUrl()
    {
        var settings = Valid();
        settings.BaseUrl = "https://connector.api.maxio.com/api/v1/billing";

        var resolved = settings.ResolveBaseAddress();

        Assert.Equal("https://connector.api.maxio.com/api/v1/billing/", resolved.ToString());
        Assert.Equal(new Uri(resolved, "subscriptions.json"), new Uri("https://connector.api.maxio.com/api/v1/billing/subscriptions.json"));
    }

    [Fact]
    public void IsConfiguredWithABaseUrlAndNoSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://example-site.chargify.com/"
        };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void ReportsEverySpecificKeyThatIsMissing()
    {
        var problems = new MaxioSettings().Problems();

        Assert.Contains(problems, problem => problem.Contains("Maxio:ApiKey", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Maxio:ProductFamilyHandle", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Maxio:Subdomain", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptySectionCountsAsAbsentRatherThanMisconfigured()
    {
        Assert.True(new MaxioSettings().IsAbsent);
        Assert.False(Valid().IsAbsent);
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var settings = Valid();
        settings.BaseUrl = "not-a-url";

        Assert.False(settings.IsConfigured);
        Assert.Contains(settings.Problems(), problem => problem.Contains("BaseUrl", StringComparison.Ordinal));
    }
}
