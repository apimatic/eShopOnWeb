using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPal;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;
using PayPal.Core.Configuration;
using PayPal.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal payment integration: strongly-typed settings with startup fail-fast, the PayPal SDK
/// client over a long-lived named <see cref="HttpClient"/>, the gateway, and the orchestration services.
/// </summary>
public static class PaymentServiceExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to boot if any credential is missing or blank (each part checked; a blank part is
        // not a missing one — [Required(AllowEmptyStrings = false)] rejects both). The message names the key,
        // never the value.
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One long-lived HttpClient with a bounded per-attempt timeout and connection recycling for DNS.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(40))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is built once at registration (captures the current secret) and held as a singleton
        // — a rotated secret takes effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalClientOptions
            {
                // The SDK exposes a single environment (Production) which is hosted on PayPal Sandbox.
                Environment = ServerEnvironment.Production,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Per-attempt timeout (a whole-call budget is enforced in the gateway via a linked token).
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                // Logging on at the host's level; request bodies (which carry card data) never logged, and
                // LoggerFactory set explicitly so the PAYPALCLIENT_LOG env var cannot turn body logging on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional verbatim base-URL override — applies to every call, including the token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new PayPalClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
