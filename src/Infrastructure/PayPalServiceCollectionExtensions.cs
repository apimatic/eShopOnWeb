using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPalServerSdk";

    /// <summary>
    /// Binds the <c>PayPal:</c> settings with startup fail-fast, registers a long-lived
    /// <see cref="PayPalServerSdkClient"/> over a named <c>HttpClient</c>, and wires the payment gateway
    /// and the payment / saved-card services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.IsSandbox || !string.IsNullOrWhiteSpace(o.BaseUrl),
                "PayPal:Environment is not 'sandbox' but PayPal:BaseUrl is not set. The SDK only ships a " +
                "Sandbox environment, so set PayPal:BaseUrl to the target environment's API base URL to " +
                "avoid sending non-sandbox traffic to the sandbox host.")
            .ValidateOnStart();

        // Named client: an explicit per-attempt Timeout bounds a hang; PooledConnectionLifetime keeps DNS
        // fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = opt.ClientId,
                    ClientSecret = opt.ClientSecret
                },
                Environment = ServerEnvironment.Sandbox,
                // Per-attempt timeout; the whole-call budget is enforced per request in the endpoints.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) },
                // Logging is assigned explicitly so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch on
                // unredacted request-body logging from outside the code; card bodies must never be logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // When set, PayPal:BaseUrl is used verbatim for every call — including the OAuth token request,
            // which the SDK resolves through this same server option.
            if (!string.IsNullOrWhiteSpace(opt.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = opt.BaseUrl!;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
