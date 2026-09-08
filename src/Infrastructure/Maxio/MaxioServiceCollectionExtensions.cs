using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Binds the <c>Maxio</c> configuration section and registers the Maxio subscription
    /// service. The underlying <see cref="MaxioAdvancedBillingClient"/> is only constructed when
    /// the section carries usable credentials (<c>ApiKey</c>, <c>Subdomain</c>,
    /// <c>ProductFamilyHandle</c>); otherwise the service is still registered and throws
    /// <see cref="MaxioNotConfiguredException"/> on first use. This keeps hosts that do not use
    /// Maxio (e.g. the integration-test host) bootable without Maxio settings.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioSection = configuration.GetSection(MaxioOptions.CONFIG_NAME);
        var maxioOptions = maxioSection.Get<MaxioOptions>() ?? new MaxioOptions();
        services.Configure<MaxioOptions>(maxioSection);

        services.AddScoped<IMaxioSubscriptionService>(serviceProvider => new MaxioSubscriptionService(
            serviceProvider.GetService<MaxioAdvancedBillingClient>(),
            serviceProvider.GetRequiredService<IOptions<MaxioOptions>>(),
            serviceProvider.GetRequiredService<ILogger<MaxioSubscriptionService>>()));

        if (!maxioOptions.IsConfigured)
        {
            return services;
        }

        services.AddHttpClient(HttpClientName, client =>
        {
            // Backstop per attempt; the SDK's own per-attempt retry timeout (below) is tighter.
            client.Timeout = TimeSpan.FromSeconds(20);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // The SDK client is registered as a singleton below, so IHttpClientFactory's handler
            // rotation never applies to it. A bounded pooled-connection lifetime keeps DNS and
            // connection state fresh for the lifetime of the process.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    // Maxio uses HTTP Basic where the username is the API key and the password
                    // is a literal "x".
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    // The transport-failure retry cannot be fully disabled (minimum is one retry);
                    // keep it minimal so a write is never resent more than once, and keep each
                    // attempt short so the whole-call budget in MaxioSubscriptionService bounds
                    // the total time.
                    MaxRetries = 1,
                    Delay = TimeSpan.FromMilliseconds(500),
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            options.Server.Production.Us.Site = maxioOptions.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        return services;
    }
}
