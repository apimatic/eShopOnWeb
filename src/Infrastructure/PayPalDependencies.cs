using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PayPalDependencies
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds the "PayPal" configuration section and registers the PayPal SDK client (singleton
    /// over a named, factory-managed HttpClient) plus the payment gateway. Credentials must be
    /// present in configuration (user-secrets / environment), never in source.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.CONFIG_NAME);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt (backstop for a hung provider); the gateway adds a total
                // call budget via a linked CancellationToken.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton: keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "via user-secrets or the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables.");
            }

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK models only the Sandbox environment; any other environment is reached
                // by pointing BaseUrl at its API base (required for non-sandbox).
                Environment = ServerEnvironment.Sandbox,
                // Credentials must be set before the client is constructed; a null Oauth2 would
                // silently produce an unauthenticated client.
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(30) // per attempt
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Used verbatim for every PayPal call, including the /v1/oauth2/token request.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }
            else if (!string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PayPal:Environment '{settings.Environment}' requires PayPal:BaseUrl to be set, " +
                    "because the SDK only models the sandbox API base address.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }
}
