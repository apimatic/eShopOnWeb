using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is not configured. Provide it via user-secrets or the PAYPAL_CLIENT_ID environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is not configured. Provide it via user-secrets or the PAYPAL_CLIENT_SECRET environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured. Provide it via user-secrets or the PAYPAL_CURRENCY environment variable.");
        }
        // This SDK release exposes only the sandbox environment; fail fast on anything else.
        if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"PayPal:Environment '{settings.Environment}' is not supported by the SDK in use; only 'sandbox' is available.");
        }

        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop; the gateway also bounds the whole call with a linked token.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

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
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override for every PayPal call, including the OAuth token request.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
