using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The registration both hosts call. A billing seam that cannot be resolved from the container is a
/// startup failure in production, so the wiring is asserted here rather than discovered at runtime.
/// </summary>
public class CompositionRootTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection();

        services.AddScoped(typeof(IAppLogger<>), typeof(NullAppLogger<>));
        services.AddMaxioBilling(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ValidConfiguration(string? baseUrl = null) => new()
    {
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = BillingTestHarness.Subdomain,
        ["Maxio:Environment"] = MaxioRegion.Us,
        ["Maxio:BaseUrl"] = baseUrl,
        ["Maxio:ProductFamilyHandle"] = BillingTestHarness.ProductFamilyHandle,
        ["Maxio:DefaultProductHandle"] = "eshop-pro",
        ["Maxio:AlternateProductHandle"] = "basic-plan",
        ["Maxio:MeteredComponentHandle"] = BillingTestHarness.MeteredComponentHandle
    };

    [Fact]
    public void Resolves_the_billing_seam_from_configuration()
    {
        using var provider = BuildProvider(ValidConfiguration());
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IBillingClient>();

        Assert.IsType<MaxioBillingClient>(client);
    }

    [Fact]
    public void Binds_the_configuration_section_onto_the_typed_settings()
    {
        using var provider = BuildProvider(ValidConfiguration(baseUrl: "http://localhost:8080"));

        var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        Assert.Equal("test-api-key", settings.ApiKey);
        Assert.Equal(BillingTestHarness.Subdomain, settings.Subdomain);
        Assert.Equal(BillingTestHarness.ProductFamilyHandle, settings.ProductFamilyHandle);
        Assert.Equal(BillingTestHarness.MeteredComponentHandle, settings.MeteredComponentHandle);

        // The configured target server survives binding intact.
        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void The_catalog_cache_is_shared_across_scopes()
    {
        using var provider = BuildProvider(ValidConfiguration());

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // Resolved handles must survive beyond one request, or every request pays to re-resolve them.
        Assert.Same(
            first.ServiceProvider.GetRequiredService<MaxioCatalogCache>(),
            second.ServiceProvider.GetRequiredService<MaxioCatalogCache>());
    }

    [Fact]
    public void A_misconfigured_integration_fails_fast_rather_than_at_the_first_customer_request()
    {
        var configuration = ValidConfiguration();
        configuration["Maxio:ApiKey"] = null;

        using var provider = BuildProvider(configuration);
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IBillingClient>());

        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
    }
}
