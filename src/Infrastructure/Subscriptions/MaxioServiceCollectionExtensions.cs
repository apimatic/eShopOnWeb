using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";
    // Protocol constant of Maxio's basic auth (the API key is the username); not a deployment setting.
    private const string BasicAuthPasswordConstant = "x";

    /// <summary>
    /// Registers the Maxio SDK client (singleton, built once) and the subscription billing service.
    /// Throws at startup when required Maxio settings are missing, so the host refuses to boot.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(settings.PerAttemptTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = BasicAuthPasswordConstant },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(settings.PerAttemptTimeoutSeconds),
                    MaxRetries = 2
                },
                // Explicit factory: bodies stay unlogged and the SDK's *_LOG env var cannot force them on.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Us.Site = settings.Subdomain!;

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
