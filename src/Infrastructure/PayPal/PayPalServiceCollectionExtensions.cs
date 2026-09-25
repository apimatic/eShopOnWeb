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

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPalServerSdk";

    /// <summary>
    /// Registers the PayPal integration: options (validated at startup), the SDK client (a long-lived
    /// singleton over a named <see cref="System.Net.Http.HttpClient"/>), the gateway, currency provider and
    /// the order-payment orchestration service.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast at startup — every credential part is checked (a blank part is not a missing one), so a
        // misconfigured deployment refuses to boot instead of surfacing as a 401 on the first call.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency) && o.Currency.Trim().Length == 3,
                "PayPal:Currency must be a 3-letter ISO-4217 code.")
            .Validate(o => string.Equals(o.Environment, "sandbox", StringComparison.OrdinalIgnoreCase),
                "PayPal:Environment must be 'sandbox' — this SDK build targets the PayPal sandbox only.")
            .Validate(o => o.CallTimeoutSeconds is >= 1 and <= 600, "PayPal:CallTimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        // A named HttpClient keeps the SDK's pipeline off the app's shared default client. Timeout here is a
        // per-attempt backstop; the overall per-call budget is enforced by a CancellationToken in the gateway.
        services.AddHttpClient(HttpClientName, (sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(options.CallTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5) // recycle connections so DNS stays fresh behind the singleton
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var sdkOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId,
                    ClientSecret = options.ClientSecret
                },
                // Per-attempt timeout matched to the call budget; POST stays out of HttpMethodsToRetry by
                // default, so PayPal writes are never silently resent by the SDK.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(options.CallTimeoutSeconds) },
                // Set LoggerFactory explicitly and keep bodies off: card data must never be logged, and this
                // stops the PAYPALSERVERSDKCLIENT_LOG env var from switching body logging on from outside.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional override: when PayPal:BaseUrl is set, use it verbatim for every call (incl. the token
            // request), instead of the environment default.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                sdkOptions.Server.Default.Sandbox.BaseUrl = options.BaseUrl!;

            return new PayPalServerSdkClient(httpClient, sdkOptions);
        });

        services.AddScoped<IPaymentConfiguration, PayPalConfiguration>();
        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}
