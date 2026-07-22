using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Registers the subscription feature: the typed billing settings, the provider client behind
/// <see cref="IBillingClient"/>, and the <see cref="ISubscriptionService"/> use-case surface.
/// </summary>
public static class ConfigureBillingServices
{
    /// <summary>Name of the factory-managed <see cref="HttpClient"/> the provider client borrows.</summary>
    public const string BillingHttpClientName = "maxio";

    public static IServiceCollection AddBillingServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.ConfigurationSection).Get<MaxioSettings>()
            ?? new MaxioSettings();

        // Fail fast: a missing credential or an undeterminable host is a deployment error, and starting up
        // only to fail on the first customer request would hide it.
        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton(new SubscriptionSettings
        {
            DefaultProductHandle = settings.DefaultProductHandle,
            AlternateProductHandle = settings.AlternateProductHandle,
            MeteredComponentHandle = settings.MeteredComponentHandle
        });

        // The base address is resolved lazily, when a client is first created, so an unconfigured billing
        // section surfaces as a configuration error on first use rather than preventing the app from
        // starting at all.
        services.AddHttpClient(BillingHttpClientName, http =>
        {
            http.BaseAddress = new Uri(settings.ResolveBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        });

        // The SDK does not own the HttpClient, so both it and the generated client stay long-lived.
        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(BillingHttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = settings.IsEuropeanRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey ?? string.Empty,
                    Password = "x"
                }
            };

            // An explicit base URL always wins; otherwise this is the subdomain-derived host. Either way it
            // is a literal URL, so the same build targets production, a sandbox tenant, or a local mock
            // purely through configuration.
            var baseUrl = settings.ResolveBaseUrl();
            if (settings.IsEuropeanRegion)
            {
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        return services;
    }
}
