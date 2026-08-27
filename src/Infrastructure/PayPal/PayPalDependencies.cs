using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalDependencies
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPal(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // A named client keeps this pipeline (timeout, handler lifetime) off the shared default.
        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the gateway adds a whole-call budget via cancellation.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal:ClientId and PayPal:ClientSecret must be configured " +
                    "(user-secrets or the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
            }

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrEmpty(settings.BaseUrl))
            {
                // Verbatim override for every call, including the OAuth token request.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }
            else if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PayPal:Environment '{settings.Environment}' requires PayPal:BaseUrl to be set explicitly; " +
                    "the SDK ships only the sandbox host.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
