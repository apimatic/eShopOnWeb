using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Composition-root wiring for the Maxio subscription feature. Both hosts call
/// <see cref="AddMaxioBilling"/>, so the provider is configured in exactly one place
/// (plan.md §2.1, §4.3).
/// </summary>
public static class MaxioBillingDependencies
{
    /// <summary>
    /// The named <see cref="HttpClient"/> the SDK is built on. Going through
    /// <see cref="IHttpClientFactory"/> keeps one pooled handler for the lifetime of the app rather
    /// than a socket per call.
    /// </summary>
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient(HttpClientName);

        // The SDK client is immutable once built and owns no per-request state, so it is a
        // singleton — matching the lifetime the SDK's own registration uses.
        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings));
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Reports a bad provider seed in the logs at startup instead of only when a customer first
        // reports usage. Purely diagnostic — it cannot fail or delay the host.
        services.AddHostedService<MaxioStartupValidator>();

        return services;
    }

    /// <summary>
    /// Builds the SDK options from configuration. This is the single point where the outbound
    /// target is decided, so retargeting production, a dev tenant, or a local mock never reaches
    /// beyond this method (plan.md §2.3).
    /// </summary>
    /// <exception cref="ApplicationCore.Exceptions.BillingConfigurationException">
    /// A required setting is missing or the configured base URL is malformed.
    /// </exception>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings)
    {
        settings.Validate();

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,

            // Maxio authenticates with the API key as the username and a literal "x" as the
            // password. The key comes from user-secrets or the environment; never from source.
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x"
            }
        };

        // An explicit Maxio:BaseUrl wins; otherwise this is the subdomain-derived host. Either way
        // the resolved value is what the SDK is told to call, so the override can never be ignored.
        var baseUrl = settings.ResolveBaseUrl();
        var site = settings.Subdomain?.Trim();

        // The US and EU server groups are distinct types, so each is configured on its own branch.
        // Kept in step with Site so that a base URL still containing the {site} placeholder resolves.
        if (settings.IsEuRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
            if (!string.IsNullOrWhiteSpace(site))
            {
                options.Server.Production.Eu.Site = site;
            }
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
            if (!string.IsNullOrWhiteSpace(site))
            {
                options.Server.Production.Us.Site = site;
            }
        }

        return options;
    }
}
