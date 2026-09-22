using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
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
/// Registers the PayPal integration: options + startup validation, a long-lived SDK client over a
/// pooled <see cref="HttpClient"/>, and the <see cref="IPayPalPaymentGateway"/> implementation.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration, bool validateOnStart)
    {
        var optionsBuilder = services
            .AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            // Each credential part is checked separately — a blank part is not a missing one. The message
            // names the config key so an operator knows what to set; it never echoes the value.
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId),
                "PayPal:ClientId is not configured (set PAYPAL_CLIENT_ID via user-secrets/environment).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret),
                "PayPal:ClientSecret is not configured (set PAYPAL_CLIENT_SECRET via user-secrets/environment).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Environment),
                "PayPal:Environment is not configured (set PAYPAL_ENVIRONMENT via user-secrets/environment).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency),
                "PayPal:Currency is not configured (set PAYPAL_CURRENCY via user-secrets/environment).");

        // Refuse to boot with a missing/blank credential in real environments. Skipped only for the
        // ASP.NET "Testing" host, which boots without live PayPal config on purpose.
        if (validateOnStart)
        {
            optionsBuilder.ValidateOnStart();
        }

        // Named HttpClient: bound per-attempt timeout + pooled connection lifetime so a long-lived
        // singleton client does not cache DNS forever. This pipeline is unshared (named, not default).
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30); // per attempt — see the gateway for the whole-call budget
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is built ONCE and captured in the singleton — a rotated secret needs a
        // process restart (documented). Logging is set explicitly (LoggerFactory assigned,
        // LogRequestBody off) so the SDK never writes raw card data and the env-var log switch is disarmed.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox, // the only environment this SDK declares
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = opts.ClientId,
                    ClientSecret = opts.ClientSecret
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,    // request bodies carry raw PAN/CVV — never log them
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional base-URL override — governs every call including the token request (the token
            // URL is resolved through the same server as operations).
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = opts.BaseUrl!.Trim();
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPayPalPaymentGateway, PayPalPaymentGateway>();

        // Payment configuration + orchestration services (scoped — they use scoped repositories).
        services.AddScoped<IPaymentConfiguration, PaymentConfiguration>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();

        return services;
    }
}
