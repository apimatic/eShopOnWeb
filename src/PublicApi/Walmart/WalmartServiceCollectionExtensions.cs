using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;
using WalmartApIs.Servers;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

public static class WalmartServiceCollectionExtensions
{
    private const string HttpClientName = "WalmartApIs";

    /// <summary>
    /// Registers the wm-v4 Walmart SDK client and the eShop inventory service. Binds <c>Walmart:</c> settings
    /// with fail-fast validation, routes all SDK traffic through a named HttpClient carrying the base-URL
    /// override handler, and keeps the SDK client long-lived (its OAuth token cache is per instance).
    /// </summary>
    public static IServiceCollection AddWalmartInventoryIntegration(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: a missing or blank ClientId/ClientSecret stops the host from starting, rather than
        // surfacing as a 401 on the first call in production.
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTransient<WalmartBaseUrlHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop on the HttpClient; the SDK's own per-attempt Retry.Timeout and the
                // service's per-request CancellationToken budget are the primary bounds.
                client.Timeout = TimeSpan.FromSeconds(100);
            })
            .AddHttpMessageHandler<WalmartBaseUrlHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<WalmartSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new WalmartApIsClientOptions
            {
                // Base URL is controlled by WalmartBaseUrlHandler (it must also redirect the absolute token
                // URL, which the SDK's server options cannot reach), so the environment stays Production.
                Environment = ServerEnvironment.Production,
                // Walmart Inventory operations authenticate with WalletAuth (OAuth2 client credentials).
                WalletAuth = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!,
                },
                // Per-attempt timeout (the default 100s is far too long for a request path).
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Assign the logger factory explicitly so WALMARTAPISCLIENT_LOG cannot switch body logging on
                // from outside the code; request bodies are never logged (LogRequestBody stays off by default).
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                },
            };

            return new WalmartApIsClient(httpClient, options);
        });

        services.AddScoped<IWalmartInventoryService, WalmartInventoryService>();

        return services;
    }
}
