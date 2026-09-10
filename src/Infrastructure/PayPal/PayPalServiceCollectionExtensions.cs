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
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal integration: settings (with startup validation), the SDK client, the gateway,
/// and the payment application services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind PayPal:* and fail fast at startup if any required part is missing/blank — rather than
        // discovering it as a 401 on the first call in production.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment), "PayPal:Environment is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "PayPal:Currency is not configured.")
            .ValidateOnStart();

        services.AddSingleton<ICurrencyProvider, ConfiguredCurrencyProvider>();

        // A named, pooled HttpClient dedicated to this SDK. Timeout here bounds one attempt; the
        // whole-call budget is enforced with a CancellationToken deadline in the gateway.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(40))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived SDK client (its OAuth token cache lives on the client). Options — including
        // credentials — are captured once here; a rotated secret takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret
                },
                // Per-attempt timeout; the CancellationToken deadline in the gateway bounds the whole call.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Set the logger factory explicitly so the PAYPALSERVERSDKCLIENT_LOG environment
                // variable cannot arm unredacted request-body logging (card data flows through here).
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // When PayPal:BaseUrl is set, use it verbatim as the base address for every call
            // (including the token request, which resolves through this same server config).
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        // Payment application services.
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
