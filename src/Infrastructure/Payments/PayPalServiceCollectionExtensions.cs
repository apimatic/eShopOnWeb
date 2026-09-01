using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client (singleton over a named, factory-managed HttpClient)
    /// and the payment gateway. Credentials must be present in <paramref name="payPalOptions"/>
    /// before the client is first resolved.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, PayPalOptions payPalOptions)
    {
        services.AddSingleton(payPalOptions);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the whole call is bounded by the caller's CancellationToken.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton: keep DNS fresh behind the long-lived client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (string.IsNullOrWhiteSpace(payPalOptions.ClientId) || string.IsNullOrWhiteSpace(payPalOptions.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(via the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables or user-secrets).");
            }

            var options = new PayPalServerSdkClientOptions
            {
                // Only the Sandbox environment is modeled by the SDK; any other target
                // (including production) is reached by overriding the base URL below.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = payPalOptions.ClientId,
                    ClientSecret = payPalOptions.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
            {
                // Used verbatim as the API base address for every call, OAuth token request included.
                options.Server.Default.Sandbox.BaseUrl = payPalOptions.BaseUrl;
            }
            else if (!string.Equals(payPalOptions.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(payPalOptions.Environment))
            {
                throw new InvalidOperationException(
                    $"PayPal:Environment '{payPalOptions.Environment}' requires PayPal:BaseUrl to be set, " +
                    "because the SDK models only the sandbox environment; the base-URL override is how any " +
                    "other environment is targeted.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, Microsoft.eShopWeb.ApplicationCore.Services.PaymentService>();

        return services;
    }
}
