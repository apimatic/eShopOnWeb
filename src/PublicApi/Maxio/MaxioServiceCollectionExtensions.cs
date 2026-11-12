using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    // Bounds ONE attempt (SDK retry timeout and HttpClient timeout); the whole-operation budget lives in
    // SubscriptionBillingService.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers the Maxio client (singleton) and the subscription billing service. Settings come from the
    /// <c>Maxio:</c> section and are validated at startup, so a missing credential stops the host from booting.
    /// The client captures its options once: a rotated key takes effect after a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        const string httpClientName = "MaxioAdvancedBilling";
        services.AddHttpClient(httpClientName, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                // Never retries POST (default HttpMethodsToRetry): the app owns write retries via reconciliation.
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
                // Explicit factory keeps the SDK log environment variable from arming body logging.
                // LogRequestBody stays off (default): customer name and email are personal data.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
            };

            // Site fills {site} in the SDK host template; an explicit BaseUrl is used verbatim instead.
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
