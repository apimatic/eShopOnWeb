using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient pipeline owned by the PayPal SDK (kept off the shared default client).</summary>
    public const string HttpClientName = "PayPal";

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableMap = new Dictionary<string, string>
    {
        ["PAYPAL_CLIENT_ID"] = "PayPal:ClientId",
        ["PAYPAL_CLIENT_SECRET"] = "PayPal:ClientSecret",
        ["PAYPAL_ENVIRONMENT"] = "PayPal:Environment",
        ["PAYPAL_CURRENCY"] = "PayPal:Currency",
    };

    /// <summary>
    /// Maps the conventional <c>PAYPAL_*</c> environment variables onto the <c>PayPal:</c> keys as the
    /// lowest-priority source, so user-secrets, appsettings and <c>PayPal__*</c> variables all override them.
    /// </summary>
    public static void AddPayPalEnvironmentVariableFallback(this IConfigurationBuilder configuration)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (variable, key) in EnvironmentVariableMap)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
        }
        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = values });
    }

    /// <summary>
    /// Registers the PayPal-backed payment capability: validated options (the host refuses to start without
    /// credentials), one long-lived SDK client, the gateway, and the domain services that use it.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, http) =>
            {
                // Bounds one attempt; the request budget bounds the whole call.
                http.Timeout = sp.GetRequiredService<IOptions<PayPalOptions>>().Value.AttemptTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One client for the process: it owns the OAuth token cache and the retry pipelines. Options are read
        // once here, so rotated credentials take effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!.Trim(),
                    ClientSecret = settings.ClientSecret!.Trim()
                },
                // GETs may be retried (twice) within the per-attempt timeout; POST/DELETE writes never are.
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = settings.AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Assigned explicitly: request bodies carry card data and must never be logged, and an unset
                    // factory would let an environment variable switch body logging on.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                TimeProvider = sp.GetRequiredService<TimeProvider>()
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Verbatim override for every call, including the OAuth token request.
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl.Trim().TrimEnd('/');
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(http, options);
        });

        services.AddScoped<PayPalRequestBudget>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentClaimStore, EfPaymentClaimStore>();
        services.AddSingleton(sp => new PaymentSettings(sp.GetRequiredService<IOptions<PayPalOptions>>().Value.Currency!));
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentMethodService>();
        services.AddScoped<ReconciliationService>();
        return services;
    }
}
