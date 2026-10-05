#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSettingsTests
{
    private static IServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioBilling(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void BindsTheMaxioSection()
    {
        var provider = BuildProvider(new()
        {
            ["Maxio:ApiKey"] = "test-api-key-not-a-secret",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family",
            ["Maxio:BaseUrl"] = "http://localhost:1/"
        });

        var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        Assert.Equal("test-site", settings.Subdomain);
        Assert.Equal("test-family", settings.ProductFamilyHandle);
        Assert.Equal("http://localhost:1/", settings.BaseUrl);
    }

    [Fact]
    public void MissingApiKeyFailsNamingTheKeyButNoValue()
    {
        var provider = BuildProvider(new()
        {
            ["Maxio:ApiKey"] = "   ",
            ["Maxio:Subdomain"] = "secret-looking-subdomain",
            ["Maxio:ProductFamilyHandle"] = "test-family"
        });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);

        Assert.Contains("Maxio:ApiKey", ex.Message);
        Assert.DoesNotContain("secret-looking-subdomain", ex.Message);
    }

    [Fact]
    public void SubdomainIsOptionalOnlyWhenBaseUrlIsSet()
    {
        var validator = new MaxioSettingsValidator();

        Assert.True(validator.Validate(null, new MaxioSettings
        {
            ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://mock.example.com"
        }).Succeeded);

        var missing = validator.Validate(null, new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" });
        Assert.True(missing.Failed);
        Assert.Contains("Maxio:Subdomain", missing.FailureMessage);
    }

    [Fact]
    public void RejectsRelativeBaseUrlAndMissingFamily()
    {
        var result = new MaxioSettingsValidator().Validate(null, new MaxioSettings { ApiKey = "k", BaseUrl = "not-a-url" });

        Assert.Contains("Maxio:BaseUrl", result.FailureMessage);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    [Fact]
    public void ClientLoggingNeverWritesRequestBodies()
    {
        var options = MaxioServiceCollectionExtensions.CreateClientOptions(
            new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f" },
            LoggerFactory.Create(_ => { }));

        Assert.False(options.Logging.LogRequestBody);
        Assert.NotNull(options.Logging.LoggerFactory);
        Assert.Equal(MaxioServiceCollectionExtensions.AttemptTimeout, options.Retry.Timeout);
        Assert.DoesNotContain(System.Net.Http.HttpMethod.Post, options.Retry.HttpMethodsToRetry);
    }
}
