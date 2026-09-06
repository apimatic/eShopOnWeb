using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptions(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(entry => entry.Key, entry => (string?)entry.Value))
            .Build());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildConfigured(params (string Key, string Value)[] extra) => Build(
        new[]
        {
            ("Maxio:ApiKey", "the-api-key"),
            ("Maxio:Subdomain", "acme"),
            ("Maxio:ProductFamilyHandle", "eshop-subscribe")
        }.Concat(extra).ToArray());

    [Fact]
    public void BindsTheMaxioSectionUsingTheDocumentedKeys()
    {
        using var provider = BuildConfigured(("Maxio:BaseUrl", "https://acme.ebilling.maxio.com"));

        var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        Assert.Equal("the-api-key", settings.ApiKey);
        Assert.Equal("acme", settings.Subdomain);
        Assert.Equal("eshop-subscribe", settings.ProductFamilyHandle);
        Assert.Equal("https://acme.ebilling.maxio.com", settings.BaseUrl);
    }

    [Fact]
    public void ConfiguresTheTypedClientWithBasicAuthenticationAndTheDerivedBaseAddress()
    {
        using var provider = BuildConfigured();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IMaxioApiClient));

        // Maxio authenticates with HTTP Basic: the API key as user name, "x" as the password.
        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.ASCII.GetBytes("the-api-key:x")),
            client.DefaultRequestHeaders.Authorization?.Parameter);
        Assert.Equal("https://acme.chargify.com/", client.BaseAddress?.ToString());
        Assert.Contains(client.DefaultRequestHeaders.Accept, header => header.MediaType == "application/json");
    }

    [Fact]
    public void HonoursTheBaseUrlOverrideOnTheTypedClient()
    {
        using var provider = BuildConfigured(("Maxio:BaseUrl", "https://acme.ebilling.maxio.com"));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IMaxioApiClient));

        Assert.Equal("https://acme.ebilling.maxio.com/", client.BaseAddress?.ToString());
    }

    [Fact]
    public void RegistersTheSubscriptionService()
    {
        using var provider = BuildConfigured();
        using var scope = provider.CreateScope();

        Assert.IsType<MaxioSubscriptionService>(scope.ServiceProvider.GetRequiredService<ISubscriptionService>());
    }

    [Fact]
    public void DefersValidationSoAHostWithoutBillingConfiguredStillStarts()
    {
        // Building the provider must not throw; the failure only surfaces when the settings are used.
        using var provider = Build();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);
    }
}
