using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client and the <see cref="IPaymentService"/> implementation.
    /// Credentials and settings are read from the <c>PayPal</c> configuration section
    /// (<c>PayPal:ClientId</c>, <c>PayPal:ClientSecret</c>, <c>PayPal:Environment</c>,
    /// <c>PayPal:BaseUrl</c>) — nothing is hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new PayPalOptions();
        configuration.GetSection(PayPalOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. via user-secrets from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }

        // Only the sandbox environment is supported by this SDK/integration. A different environment is
        // only reachable by supplying a verbatim base URL override.
        if (!string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{options.Environment}' is not supported. Use 'sandbox', " +
                "or supply an explicit PayPal:BaseUrl to target a different host.");
        }

        services.AddSingleton(options);

        var callTimeout = TimeSpan.FromSeconds(options.CallTimeoutSeconds > 0 ? options.CallTimeoutSeconds : 30);

        // A dedicated, long-lived HttpClient pipeline scoped to the SDK (kept off the shared default client).
        // Timeout bounds a single attempt; PooledConnectionLifetime keeps DNS fresh behind the singleton client.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = callTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId!,
                    ClientSecret = options.ClientSecret!
                },
                Retry = RetryOptions.Default() with { Timeout = callTimeout }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = options.BaseUrl! }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentService, PayPalPaymentService>();

        return services;
    }
}
