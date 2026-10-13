using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;
using WalmartApIs.Servers;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class WalmartConfiguration
{
    private const string HttpClientName = "Walmart";

    public static IServiceCollection AddWalmartIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to boot when either credential part is missing/blank (a blank part is not
        // a missing one, so both are checked), rather than surfacing it as a 401 on the first call.
        services.AddOptions<WalmartSettings>()
            .Bind(configuration.GetSection(WalmartSettings.ConfigSection))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "Walmart:ClientId is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "Walmart:ClientSecret is not configured.")
            .ValidateOnStart();

        // A long-lived HttpClient with a recycling connection pool (needed behind the singleton SDK
        // client) and a per-attempt backstop timeout.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived client; the OAuth token cache lives on it. Options (incl. the secret) are
        // captured once here — a rotated secret takes effect on the next process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<WalmartSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new WalmartApIsClientOptions
            {
                Environment = ServerEnvironment.Production,
                // Inventory operations authenticate with WalletAuth (OAuth2 client credentials).
                WalletAuth = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                },
                // Per-attempt timeout (the default is 100s); the whole-call budget is enforced in the service.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
                // Assign the factory explicitly so the WALMARTAPISCLIENT_LOG env var cannot switch
                // request-body logging on from outside the code; bodies stay off regardless.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            ApplyBaseUrlOverride(options, settings.BaseUrl);

            return new WalmartApIsClient(httpClient, options);
        });

        services.AddScoped<IWalmartMarketplaceService, WalmartMarketplaceService>();
        return services;
    }

    /// <summary>
    /// When <c>Walmart:BaseUrl</c> is set, replaces the scheme/host/port of every Walmart server
    /// group (so both operation calls and token acquisition are redirected), keeping the base path
    /// the contract carries. Only the active (Production) environment's URLs are read by the SDK.
    /// </summary>
    private static void ApplyBaseUrlOverride(WalmartApIsClientOptions options, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var over) || string.IsNullOrEmpty(over.Host))
        {
            throw new InvalidOperationException(
                $"Walmart:BaseUrl '{baseUrl}' is not a valid absolute scheme://host[:port] URL.");
        }

        var authority = over.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        options.Server.Default.Production.BaseUrl = Rewrite(options.Server.Default.Production.BaseUrl, authority);
        options.Server.Default1.Production.BaseUrl = Rewrite(options.Server.Default1.Production.BaseUrl, authority);
    }

    private static string Rewrite(string contractBaseUrl, string authority)
    {
        var contract = new Uri(contractBaseUrl, UriKind.Absolute);
        return authority + contract.AbsolutePath.TrimEnd('/');
    }
}
