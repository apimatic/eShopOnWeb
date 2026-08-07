using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal integration (SDK client + <see cref="IPayPalGateway"/>) into the service container.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("PayPal");

        // Bind for consumers that want IOptions<PayPalSettings> ...
        services.Configure<PayPalSettings>(section);
        // ... and a concrete instance for immediate use while building the singleton client below.
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();

        // A named HttpClient keeps this SDK's timeout / primary handler off the shared default client.
        // Timeout is per-attempt and must be set explicitly (the 100s default is an outage on a checkout path);
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived (singleton) SDK client below.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is lightweight controller wrappers over the HTTP pipeline — register it once
        // (singleton) and reuse it for the app lifetime.
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Per-attempt timeout aligned with the HttpClient backstop above.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            // Optional base-URL override — the SDK re-resolves the URL per request from options.Server, so set
            // it before constructing the client. Applies to the Sandbox environment selected above.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        // Gateway is scoped: it depends on IAppLogger<T>, which is registered scoped, so it cannot be a singleton.
        services.AddScoped<IPayPalGateway, PayPalGateway>();

        return services;
    }
}
