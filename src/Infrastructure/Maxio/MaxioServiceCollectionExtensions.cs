using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers Maxio Advanced Billing: the strongly-typed <see cref="MaxioSettings"/>, a dedicated
/// (named) <see cref="HttpClient"/> carrying the write-idempotency guard, the long-lived SDK client,
/// and the <see cref="ISubscriptionBillingService"/> implementation.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The SDK does not own the HttpClient. Use a dedicated named client (isolated from other
        // consumers) and bound the pooled connection lifetime so DNS changes are picked up even
        // though the SDK client is long-lived.
        services.AddTransient<MaxioSingleWriteAttemptHandler>();
        services
            .AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioSingleWriteAttemptHandler>();

        // The SDK client is lightweight controller wrappers over the shared pipeline: build once,
        // reuse for the app lifetime.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = BuildOptions(settings);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) must be configured.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            // Basic auth: username = API key, password = literal "x" (not a secret).
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            }
        };

        // Map the configured environment string to the SDK's server-environment selector ourselves,
        // defaulting to US: environment/server selector enums are not guaranteed to expose FromValue.
        var isEu = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        options.Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us;

        if (isEu)
        {
            options.Server.Production.Eu.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
        }

        return options;
    }
}
