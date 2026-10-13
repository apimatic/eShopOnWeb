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
    private const string SdkClientName = "Walmart";
    private const string TokenClientName = "WalmartToken";

    /// <summary>
    /// Registers the Walmart Marketplace stock integration: fail-fast settings binding, the
    /// SDK client (singleton over a pooled named <see cref="HttpClient"/>, with the base-URL
    /// override and explicit logging), the token strategy that honours the override, and the
    /// inventory service.
    /// </summary>
    public static IServiceCollection AddWalmartIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Refuse to boot when a credential is missing or blank, rather than discovering it as a
        // 401 on the first call. Both parts are checked (a blank part is not a missing one).
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.ConfigSection))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(SdkClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddHttpClient(TokenClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<WalmartSettings>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var tokenHttpClient = httpClientFactory.CreateClient(TokenClientName);
            var sdkHttpClient = httpClientFactory.CreateClient(SdkClientName);

            var options = new WalmartApIsClientOptions
            {
                Environment = ServerEnvironment.Production,
                WalletAuth = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
                // Route token acquisition through the Walmart:BaseUrl override too.
                WalletAuthTokenStrategy = new WalletClientCredentialsTokenStrategy(tokenHttpClient, settings.BaseUrl),
                // Per-attempt timeout; the service adds a whole-call deadline on top.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Assign the factory explicitly so WALMARTAPISCLIENT_LOG cannot switch body logging
                // on from outside the code; bodies are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                },
            };

            // Apply the optional base-URL override to every API server group, preserving each
            // contract base path. When unset, the SDK targets production.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl =
                    WalmartBaseUrlOverride.Apply(options.Server.Default.Production.BaseUrl, settings.BaseUrl);
                options.Server.Default1.Production.BaseUrl =
                    WalmartBaseUrlOverride.Apply(options.Server.Default1.Production.BaseUrl, settings.BaseUrl);
            }

            return new WalmartApIsClient(sdkHttpClient, options);
        });

        services.AddSingleton<IWalmartInventoryService, WalmartInventoryService>();

        return services;
    }
}
