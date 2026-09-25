using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds <c>PayPal:</c> settings (with fail-fast validation), registers a long-lived
    /// <see cref="PayPalServerSdkClient"/> over a named <see cref="System.Net.Http.IHttpClientFactory"/>
    /// client, and registers the gateway and payment services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start if any credential part is missing/blank, or if a non-sandbox
        // environment is configured (the SDK exposes only sandbox), or if BaseUrl is set but not absolute.
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            // Each credential part is checked separately — a blank part is not the same as a missing one.
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PayPal:ClientId is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PayPal:ClientSecret is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment), "PayPal:Environment is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency), "PayPal:Currency is not configured.")
            .Validate(o => string.Equals(o.Environment, "sandbox", StringComparison.OrdinalIgnoreCase),
                "PayPal:Environment must be 'sandbox' — this build targets the PayPal sandbox only.")
            .Validate(o => string.IsNullOrWhiteSpace(o.BaseUrl) || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "PayPal:BaseUrl must be an absolute URL when set.")
            .ValidateOnStart();

        // Currency for the application layer.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            return new PaymentSettings { CurrencyCode = options.Currency };
        });

        // A named HttpClient scoped to this SDK: a per-attempt Timeout, and a pooled-connection lifetime
        // so DNS stays fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived SDK client. Options (incl. credentials) are captured once at registration; a
        // rotated secret takes effect on process restart. Logging is wired to the host's factory with
        // request-body logging OFF and the LoggerFactory set explicitly, so the SDK's log environment
        // variable cannot switch unredacted card bodies on from outside the code.
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = o.ClientId,
                    ClientSecret = o.ClientSecret
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional override: when set, use verbatim as the base address for EVERY call, including the
            // OAuth token request (AuthSchemes builds the token URL through this same Server.Default node).
            if (!string.IsNullOrWhiteSpace(o.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = o.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalGateway>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
