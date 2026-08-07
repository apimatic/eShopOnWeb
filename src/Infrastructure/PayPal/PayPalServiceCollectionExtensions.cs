using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;   // OAuth2ClientCredentials
using PayPalServerSdk.Servers;                                       // ServerEnvironment

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers the PayPal SDK client and the <see cref="IPayPalPaymentGateway"/> implementation.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    // Named HttpClient keeps this SDK's pipeline (timeout, primary handler) off the shared default client.
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from the <c>PayPal</c> configuration section, registers a long-lived
    /// <see cref="PayPalServerSdkClient"/> (OAuth2 client-credentials, sandbox, optional base-URL override) over an
    /// <see cref="IHttpClientFactory"/>-managed <see cref="HttpClient"/>, and registers
    /// <see cref="IPayPalPaymentGateway"/> as scoped.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        ValidateSettings(settings);

        // The SDK client is meant to be long-lived (lightweight controller wrappers over the HTTP pipeline).
        // A named HttpClient from IHttpClientFactory owns the socket lifetime; PooledConnectionLifetime keeps
        // DNS fresh behind this singleton, and an explicit Timeout bounds a hung provider (default is 100s).
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,   // the only environment this SDK exposes
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // Optional verbatim base-URL override (mock server / proxy / self-hosted gateway).
            // Must be set on the environment selected above, before the client is constructed.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    private static void ValidateSettings(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal is not configured: 'PayPal:ClientId' and 'PayPal:ClientSecret' are required.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        var isSandbox = string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase);

        if (!isSandbox && !hasBaseUrl)
        {
            throw new InvalidOperationException(
                $"PayPal environment '{settings.Environment}' is not supported. Only 'sandbox' is available; " +
                "to target another host, set 'PayPal:BaseUrl' with an explicit base-URL override.");
        }
    }
}
