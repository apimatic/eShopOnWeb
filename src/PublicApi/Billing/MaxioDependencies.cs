using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioDependencies
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and the subscription service.
    /// Configuration is bound from the <see cref="MaxioSettings.SectionName"/> section;
    /// credentials come from user-secrets or environment variables, never source.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName));

        services.AddSingleton<SingleSendHandler>();

        // Named client keeps this SDK's timeout and handlers off the shared default client.
        // Timeout bounds one attempt; the singleton client below pins handlers, so the
        // pooled connection lifetime keeps DNS fresh.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<SingleSendHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            Validate(settings);

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    Password = "x"
                },
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override; used exactly as configured.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            return new MaxioAdvancedBillingClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static void Validate(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY user-secret or environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY user-secret or environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Either Maxio:BaseUrl or Maxio:Subdomain must be configured. Set the MAXIO_SITE_SUBDOMAIN user-secret or environment variable (or provide a base URL override).");
        }
    }
}