using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing SDK client and the subscription service that fronts it.
    /// Binds from configuration only ('Maxio:ApiKey' / 'Maxio:Subdomain' / 'Maxio:ProductFamilyHandle' /
    /// 'Maxio:BaseUrl') — no credentials are hard-coded. Registration never throws on missing/blank
    /// configuration (so hosts and test fixtures that don't exercise the subscribe feature still start
    /// cleanly); IMaxioSubscriptionService raises MaxioProviderException on first use instead.
    /// </summary>
    public static IServiceCollection AddMaxioAdvancedBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.ConfigSectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds a single attempt; the overall call is bounded by the CancellationToken the
                // caller passes through (see dotnet-configuration-resilience).
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton built from one CreateClient() call, so this keeps
                // DNS/connection state from going stale for the lifetime of the process.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioSubscribeGate>();
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
