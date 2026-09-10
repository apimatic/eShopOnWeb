using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers PayPal payment support: fail-fast configuration binding, the SDK client (over a dedicated
    /// named <see cref="HttpClient"/>), the payment gateway, and the order-payment / saved-card services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind PayPal:* and refuse to boot if any credential/setting is missing or blank.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();

        // A dedicated HttpClient keeps this SDK's timeout/pooling off the shared default client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // keep DNS fresh behind the long-lived client.
            });

        // The SDK client is a long-lived singleton (holds the OAuth token cache and resilience pipelines).
        services.AddSingleton<PayPalServerSdkClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret,
                },
                // Set LoggerFactory explicitly so the PAYPALSERVERSDKCLIENT_LOG env var can never switch
                // request-body logging on from outside the code (requests carry card data).
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                // Record each response's HTTP status so the gateway can key error handling on it.
                Hooks = new[]
                {
                    SdkHook.OnResponse((response, _) =>
                        PayPalResponseContext.Record((int)response.StatusCode)),
                },
            };

            // Optional base-URL override: applies to every call, including the OAuth token request
            // (AuthSchemes resolves the token endpoint through Server.Default(...)).
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}
