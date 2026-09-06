using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioServiceCollectionExtensionsTests
{
    /// <summary>The name AddHttpClient&lt;TClient, TImplementation&gt; derives for a typed client.</summary>
    private const string TypedClientName = nameof(IMaxioApiClient);

    [Fact]
    public void ConfiguresBasicAuthenticationAndTheDerivedBaseAddress()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "acme",
            ["Maxio:ProductFamilyHandle"] = "demo-family"
        });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TypedClientName);

        Assert.Equal("https://acme.chargify.com/", client.BaseAddress?.AbsoluteUri);

        // openapi.yaml, components.securitySchemes.BasicAuth: username = API key, password = "x".
        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test-api-key:x")),
            client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public void PrefersTheBaseUrlOverrideOverTheSubdomain()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "acme",
            ["Maxio:ProductFamilyHandle"] = "demo-family",
            ["Maxio:BaseUrl"] = "https://billing-gateway.internal/maxio/"
        });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TypedClientName);

        Assert.Equal("https://billing-gateway.internal/maxio/", client.BaseAddress?.AbsoluteUri);
    }

    [Fact]
    public void StaysRegistrableWithoutCredentialsSoTheHostStillStarts()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        // Resolving must not throw: the failure is reported per-request, with the offending keys named.
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();

        Assert.NotNull(service);
        Assert.Null(provider.GetRequiredService<IHttpClientFactory>().CreateClient(TypedClientName).BaseAddress);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        return services.BuildServiceProvider(validateScopes: true);
    }
}
