using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal SDK client and the <see cref="IPaymentGateway"/> implementation.
    /// Binds the <c>PayPal:</c> configuration section; credentials/environment/base-url all come
    /// from configuration and are never hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));

        // Named HttpClient keeps the timeout, status handler and primary handler scoped to PayPal
        // (off the shared default client). Timeout bounds a single attempt; the whole-call budget
        // is applied via a CancellationToken at the gateway.
        services.AddTransient<PayPalResponseStatusHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<PayPalResponseStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so IHttpClientFactory handler rotation never
                // reaches it; this keeps pooled connections (and DNS) from going stale.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is lightweight controller wrappers over the HTTP pipeline — construct once.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // This SDK exposes only the Sandbox environment (see plan Blockers). This task
                // targets sandbox exclusively.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty
                },
                // These operations are non-idempotent writes; minimise the retry surface (the
                // single-send handler already blocks any resend) and bound each attempt.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            // Optional explicit base-URL override, applied verbatim when set.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Sandbox = new DefaultOptions.SandboxOptions
                        {
                            BaseUrl = settings.BaseUrl
                        }
                    }
                };
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
