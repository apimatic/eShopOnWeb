using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound; the whole-call budget lives in <see cref="MaxioSubscriptionService"/>.</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Registers the Maxio client (singleton, built once from the "Maxio" section) and the subscription service.
    /// Throws at registration, i.e. host startup, when a setting is missing or blank.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        var problem = settings.Validate();
        if (problem != null)
        {
            throw new InvalidOperationException(problem);
        }

        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Options are built once here: a rotated key needs a restart.
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout, MaxRetries = 2 },
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim; assigned on the node of the selected environment (Us, the default).
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
