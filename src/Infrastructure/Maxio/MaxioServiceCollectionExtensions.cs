using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wires up the Maxio-backed subscription capability. Additive: it registers only new services and
/// changes nothing about the existing catalog, basket or order registrations.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    /// <summary>Environment variables recognised as an alternative to <c>Maxio:*</c> configuration keys.</summary>
    private static readonly (string EnvironmentVariable, string ConfigurationKey)[] EnvironmentAliases =
    {
        ("MAXIO_API_KEY", "Maxio:ApiKey"),
        ("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain"),
        ("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle"),
        ("MAXIO_BASE_URL", "Maxio:BaseUrl")
    };

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MaxioSettings.ConfigurationSection);

        // Validation is deliberately not run at host start. Subscriptions are an additive capability:
        // a deployment that does not use them must still be able to boot the catalog, basket and
        // order endpoints. Instead the settings are validated the first time the subscription
        // capability is used, and a misconfiguration is logged loudly at start-up by
        // MaxioConfigurationHealthCheck.
        services.AddOptions<MaxioSettings>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.BaseUrl) || settings.ResolvesBaseAddress(out _),
                "Maxio:BaseUrl must be a valid absolute URL when it is set.");

        services.AddSingleton(sp =>
        {
            var options = new SubscriptionOptions();
            section.Bind(options);
            return options;
        });

        services.AddHostedService<MaxioConfigurationHealthCheck>();

        services.AddSingleton<MaxioSiteCache>();
        services.AddSingleton<KeyedAsyncLock>();

        services.AddTransient<MaxioAuthenticationHandler>();
        services.AddTransient<MaxioTransientFaultHandler>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.BaseAddress = settings.ResolveBaseAddress();

                // Per-request timeouts are enforced by the client itself so a slow call can be
                // reported as such; the message-handler timeout is only a backstop.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Subscriptions/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            .AddHttpMessageHandler<MaxioTransientFaultHandler>()
            .AddHttpMessageHandler<MaxioAuthenticationHandler>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<IBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriberDirectory, IdentitySubscriberDirectory>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }

    /// <summary>
    /// Maps the <c>MAXIO_*</c> environment variables this deployment supplies onto the
    /// <c>Maxio:*</c> configuration keys, so a container or CI job can configure the integration
    /// without also having to know the double-underscore convention. Added last, which gives it the
    /// same precedence environment variables normally have over files and user-secrets.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mapped = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentAliases)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                mapped[key] = value;
            }
        }

        if (mapped.Count > 0)
        {
            builder.AddInMemoryCollection(mapped);
        }

        return builder;
    }

    private static bool ResolvesBaseAddress(this MaxioSettings settings, out Uri? baseAddress)
    {
        try
        {
            baseAddress = settings.ResolveBaseAddress();
            return true;
        }
        catch (InvalidOperationException)
        {
            baseAddress = null;
            return false;
        }
    }
}
