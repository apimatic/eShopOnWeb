using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing integration: the SDK client (built from the
/// <c>Maxio</c> configuration section) and the <see cref="ISubscriptionBillingService"/> adapter.
/// </summary>
public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // The SDK client is long-lived and thread-safe: register it once as a singleton. It owns a
        // single HttpClient with a bounded pooled-connection lifetime so a process-lifetime client
        // still picks up DNS changes (IHttpClientFactory handler rotation does not apply to a client
        // resolved once and held). Configuration is validated lazily on first resolution, so hosts
        // that never touch the subscription feature are unaffected by absent Maxio settings.
        services.AddSingleton<MaxioAdvancedBillingClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio:ApiKey is not configured. Provide it via user-secrets or environment (from MAXIO_API_KEY).");
            }

            var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
            if (!hasBaseUrl && string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                throw new InvalidOperationException(
                    "Maxio requires either Maxio:BaseUrl or Maxio:Subdomain to be configured.");
            }

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    Password = "x",
                },
            };

            if (hasBaseUrl)
            {
                // Explicit override: use the base URL verbatim instead of deriving from the subdomain.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            // Bound the pooled connection lifetime (a process-lifetime singleton misses factory
            // handler rotation), then wrap it with the single-send guard so a transport retry
            // cannot silently re-POST a create.
            var primaryHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            var guardHandler = new SingleSendWriteGuardHandler(primaryHandler);
            var httpClient = new HttpClient(guardHandler);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }
}
