using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal integration: strongly-typed settings, a long-lived <see cref="PayPalServerSdkClient"/>
/// (OAuth2 client-credentials), and the <see cref="IPayPalGateway"/> implementation.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    // PayPal's public API hosts. The SDK exposes only a Sandbox ServerEnvironment, so Production is
    // reached by overriding the base URL (which also governs the OAuth2 token request).
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string ProductionBaseUrl = "https://api-m.paypal.com";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);

        // Bind for consumers that resolve IOptions<PayPalSettings> ...
        services.Configure<PayPalSettings>(section);

        // ... and read a concrete instance now, both to configure the SDK client at registration time
        // and to inject the gateway's currency-default dependency.
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        services.AddSingleton(settings);

        // A named HttpClient keeps this SDK's timeout/handler off the shared default factory client.
        // Timeout bounds one attempt (default 100s is an outage on an interactive path); PooledConnectionLifetime
        // keeps DNS fresh behind the singleton client below (IHttpClientFactory rotation never reaches a
        // singleton that calls CreateClient() once).
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox, // sole environment the SDK exposes
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty,
                    // The SDK fetches, caches, and refreshes the token automatically for this scheme.
                },
            };

            // Configure the base URL BEFORE constructing the client. It is re-resolved on every request —
            // including the /v1/oauth2/token exchange — so this one value governs the whole SDK.
            var baseUrl = ResolveBaseUrl(settings);
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        return services;
    }

    /// <summary>
    /// A non-empty PayPal:BaseUrl wins verbatim. Otherwise, because the SDK has no Production environment,
    /// PayPal:Environment == "Production" maps to the live host; anything else keeps the sandbox default.
    /// </summary>
    private static string? ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.Trim();
        }

        if (string.Equals(settings.Environment?.Trim(), "Production", StringComparison.OrdinalIgnoreCase))
        {
            return ProductionBaseUrl;
        }

        return SandboxBaseUrl;
    }
}
