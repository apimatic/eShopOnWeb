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

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    /// <summary>
    /// Registers PayPal: settings (fail-fast at startup), a single long-lived SDK client over an
    /// IHttpClientFactory-managed HttpClient, the gateway, and the payment services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind settings and refuse to boot when any required part is missing/blank.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalSettings>, PayPalSettingsValidator>();

        // A named HttpClient: a real per-attempt timeout, and a pooled-connection lifetime so a
        // long-lived (singleton) SDK client does not pin a stale DNS entry.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is built once at registration (captures the secret; a rotated secret needs
        // a process restart) and kept for the process lifetime (it also caches the OAuth token).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                },
                // Card data flows in request bodies: keep body logging OFF and set LoggerFactory
                // explicitly so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch it on externally.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                },
                // Per-attempt timeout; the whole-call budget is the CancellationToken from the caller.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
            };

            // Optional base-URL override, applied verbatim to the sandbox node. Because the OAuth
            // token URL is resolved from this same server node, the override also covers the token
            // request — as required.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
