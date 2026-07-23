using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The composition-root contract. These tests build the container the way the hosts do, with scope and
/// build-time validation switched on, so a lifetime mistake in the billing registrations fails here rather
/// than crashing the storefront on startup.
/// </summary>
public class MaxioBillingDependenciesTests
{
    private static IServiceProvider BuildProvider(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "cp-exp-2",
            ["Maxio:Environment"] = "US",
            ["Maxio:BaseUrl"] = BillingTestContext.MockBaseUrl,
            ["Maxio:ProductFamilyHandle"] = "eshop-subscribe",
            ["Maxio:DefaultProductHandle"] = "eshop-pro",
            ["Maxio:AlternateProductHandle"] = "basic-plan",
            ["Maxio:MeteredComponentHandle"] = "api-call"
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
        services.AddMaxioBilling(configuration);

        // Exactly the validation the hosts perform in Development.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public void AddMaxioBilling_BuildsWithoutLifetimeViolations()
    {
        // A singleton consuming a scoped dependency would throw here — and would otherwise only surface
        // as a failure to start the whole application.
        using var provider = (ServiceProvider)BuildProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddMaxioBilling_RegistersTheMaxioClientBehindTheProviderAgnosticInterface()
    {
        using var provider = (ServiceProvider)BuildProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IBillingClient>();

        Assert.IsType<MaxioBillingClient>(client);
    }

    [Fact]
    public void AddMaxioBilling_BindsTheTypedOptionsFromConfiguration()
    {
        using var provider = (ServiceProvider)BuildProvider();

        var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;

        Assert.Equal("test-key", settings.ApiKey);
        Assert.Equal("cp-exp-2", settings.Subdomain);
        Assert.Equal("eshop-subscribe", settings.ProductFamilyHandle);
        Assert.Equal("api-call", settings.MeteredComponentHandle);
    }

    [Fact]
    public void AddMaxioBilling_ExposesTheHandlesToTheDomainThroughItsOwnAbstraction()
    {
        using var provider = (ServiceProvider)BuildProvider();

        // ApplicationCore must be able to read the handles without knowing about MaxioSettings.
        var settings = provider.GetRequiredService<ISubscriptionSettings>();

        Assert.Equal("eshop-pro", settings.DefaultProductHandle);
        Assert.Equal("basic-plan", settings.AlternateProductHandle);
        Assert.Equal("api-call", settings.MeteredComponentHandle);
    }

    [Fact]
    public void AddMaxioBilling_RegistersTheStartupValidator()
    {
        using var provider = (ServiceProvider)BuildProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), service => service is MaxioStartupValidator);
    }

    [Fact]
    public void AddMaxioBilling_SharesOneCatalogCacheAcrossScopes()
    {
        using var provider = (ServiceProvider)BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // The cache must be a singleton, or the family lookup would repeat on every request.
        Assert.Same(
            first.ServiceProvider.GetRequiredService<MaxioCatalogCache>(),
            second.ServiceProvider.GetRequiredService<MaxioCatalogCache>());
    }

    [Fact]
    public void AddMaxioBilling_PointsTheHttpClientAtTheConfiguredTarget()
    {
        using var provider = (ServiceProvider)BuildProvider(new Dictionary<string, string?>
        {
            ["Maxio:BaseUrl"] = "http://localhost:9099"
        });

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var httpClient = factory.CreateClient(nameof(IBillingClient));

        Assert.Equal(new Uri("http://localhost:9099"), httpClient.BaseAddress);
    }

    [Fact]
    public void AddMaxioBilling_DerivesTheTargetFromTheSubdomain_WhenNoOverrideIsConfigured()
    {
        using var provider = (ServiceProvider)BuildProvider(new Dictionary<string, string?>
        {
            ["Maxio:BaseUrl"] = null
        });

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var httpClient = factory.CreateClient(nameof(IBillingClient));

        Assert.Equal(new Uri("https://cp-exp-2.chargify.com"), httpClient.BaseAddress);
    }
}
