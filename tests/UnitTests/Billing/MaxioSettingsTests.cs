using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

public class MaxioSettingsTests
{
    [Fact]
    public void AcceptsAFullyConfiguredSection()
    {
        Assert.Empty(MaxioTestHarness.Settings().Validate());
    }

    [Fact]
    public void ReportsEveryMissingSetting()
    {
        var errors = new MaxioSettings().Validate();

        Assert.Contains(errors, e => e.Contains("Maxio:ApiKey"));
        Assert.Contains(errors, e => e.Contains("Maxio:Subdomain"));
        Assert.Contains(errors, e => e.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void TreatsAnExplicitBaseUrlAsAReplacementForTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://maxio.test"
        };

        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var settings = MaxioTestHarness.Settings(baseUrl: "not-a-url");

        Assert.Contains(settings.Validate(), e => e.Contains("Maxio:BaseUrl"));
    }
}
