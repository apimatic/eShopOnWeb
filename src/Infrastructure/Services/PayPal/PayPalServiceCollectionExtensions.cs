using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPalServerSdk";

    /// <summary>
    /// Registers the PayPal integration: options with startup fail-fast validation, a long-lived
    /// SDK client over a named <see cref="IHttpClientFactory"/> client, the gateway, and the
    /// payment application services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind PayPal:* and refuse to boot if any required credential is missing/blank (fail-fast).
        // Each part is checked separately — a blank part is not a missing one — and the message names
        // the exact config key so an operator knows what to set, without echoing the value.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId),
                "PayPal:ClientId is not configured. Set it via user-secrets, environment, or your secret store.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret),
                "PayPal:ClientSecret is not configured. Set it via user-secrets, environment, or your secret store.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment),
                "PayPal:Environment is not configured. Set it via user-secrets, environment, or your secret store.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency),
                "PayPal:Currency is not configured. Set it via user-secrets, environment, or your secret store.")
            .ValidateOnStart();

        // Named HttpClient: per-attempt timeout backstop + pooled-connection recycling so a long-lived
        // singleton client does not cache DNS forever.
        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived (holds the OAuth token cache and resilience pipelines).
        // Options are captured once here, so a rotated secret takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var sdkOptions = new PayPalServerSdkClientOptions
            {
                // The SDK declares only Sandbox; a different account/host is expressed via PayPal:BaseUrl.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret
                },
                // Assign LoggerFactory explicitly so the PAYPALSERVERSDKCLIENT_LOG environment variable
                // cannot switch on unredacted request-body logging (cards flow through these requests).
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false
                }
            };

            // When PayPal:BaseUrl is set, use it verbatim as the base for every call (token included).
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                sdkOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl!.Trim();
            }

            return new PayPalServerSdkClient(httpClient, sdkOptions);
        });

        services.AddScoped<IPaymentGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
