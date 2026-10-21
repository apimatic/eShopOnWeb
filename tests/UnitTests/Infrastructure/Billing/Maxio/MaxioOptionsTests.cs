using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "k",
        Subdomain = "acme",
        ProductFamilyHandle = "family",
    };

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Subdomain")]
    [InlineData("ProductFamilyHandle")]
    public void MissingOrBlankRequiredValueFailsNamingTheKey(string key)
    {
        var options = Valid();
        typeof(MaxioOptions).GetProperty(key)!.SetValue(options, "  ");

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Maxio:" + key, ex.Message);
    }

    [Fact]
    public void BaseUrlReplacesTheSubdomainRequirement()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://billing-proxy.example.com";

        options.Validate();

        Assert.Equal("billing-proxy.example.com", options.RequestLimitKey);
    }

    [Fact]
    public void RelativeBaseUrlIsRefused()
    {
        var options = Valid();
        options.BaseUrl = "not a url";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(null, "US")]
    [InlineData("eu", "EU")]
    public void EnvironmentDefaultsToUs(string? configured, string expected)
    {
        var options = Valid();
        options.Environment = configured;

        Assert.Equal(expected, options.ResolveEnvironment().Value);
    }

    [Fact]
    public void UnknownEnvironmentIsRefused()
    {
        var options = Valid();
        options.Environment = "mars";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void RegistrationFailsFastWhenTheSectionIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddMaxioSubscriptionBilling(configuration));
    }

    [Fact]
    public void ClientTargetsTheConfiguredSiteInEveryServerGroup()
    {
        var clientOptions = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions();

        MaxioServiceCollectionExtensions.ConfigureClient(clientOptions, Valid(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.Equal(ServerEnvironment.Us, clientOptions.Environment);
        Assert.Equal("acme", clientOptions.Server.Production.Us.Site);
        Assert.Equal("acme", clientOptions.Server.Production.Eu.Site);
        Assert.Equal("x", clientOptions.BasicAuth!.Password);
        Assert.False(clientOptions.Logging.LogRequestBody);
        Assert.NotNull(clientOptions.Logging.LoggerFactory);
        Assert.DoesNotContain(HttpMethod.Put, clientOptions.Retry.HttpMethodsToRetry);
        Assert.DoesNotContain(HttpMethod.Post, clientOptions.Retry.HttpMethodsToRetry);
        Assert.DoesNotContain((System.Net.HttpStatusCode)429, clientOptions.Retry.StatusCodesToRetry);
        Assert.Equal(TimeSpan.FromSeconds(130), clientOptions.Retry.Timeout);
    }
}
