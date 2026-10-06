using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "k",
        Subdomain = "site",
        ProductFamilyHandle = "family"
    };

    [Fact]
    public void ValidSettingsHaveNoErrors() => Assert.Empty(Valid().Validate());

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("ProductFamilyHandle")]
    public void BlankRequiredValueIsReportedByKeyWithoutEchoingValues(string key)
    {
        var settings = Valid();
        typeof(MaxioSettings).GetProperty(key)!.SetValue(settings, "   ");

        var error = Assert.Single(settings.Validate());

        Assert.Contains($"Maxio:{key}", error);
    }

    [Fact]
    public void SubdomainIsOptionalOnlyWhenBaseUrlIsSet()
    {
        var settings = Valid();
        settings.Subdomain = null;
        Assert.Contains(settings.Validate(), e => e.Contains("Maxio:Subdomain"));

        settings.BaseUrl = "https://proxy.example.com";
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void TotalWaitBudgetMayNotExceedThirtySeconds()
    {
        var settings = Valid();
        settings.RequestBudgetSeconds = 28;
        settings.SettleBudgetSeconds = 4;

        Assert.Contains(settings.Validate(), e => e.Contains("must not exceed 30"));
    }

    [Fact]
    public void UnknownCollectionMethodIsRejected()
    {
        var settings = Valid();
        settings.PaymentCollectionMethod = "cash";

        Assert.Contains(settings.Validate(), e => e.Contains("PaymentCollectionMethod"));
    }

    [Fact]
    public void HostRefusesToStartWithoutCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Maxio:ProductFamilyHandle"] = "family" })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);

        Assert.Contains(ex.Failures, f => f.Contains("Maxio:ApiKey"));
        Assert.Contains(ex.Failures, f => f.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void BaseUrlOverridesTheDerivedHostVerbatim()
    {
        var settings = Valid();
        settings.BaseUrl = "https://proxy.example.com/maxio/";

        var options = MaxioBillingServiceCollectionExtensions.CreateClientOptions(settings, LoggerFactory.Create(_ => { }));

        Assert.Equal("https://proxy.example.com/maxio", options.Server.Production.Us.BaseUrl);
        Assert.Equal("site", options.Server.Production.Us.Site);
        Assert.NotNull(options.Logging.LoggerFactory);
        Assert.False(options.Logging.LogRequestBody);
    }
}
