using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Services;
using PayPal;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;
using PayPal.Core.Configuration;
using PayPal.Servers;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PayPalServiceCollectionExtensions
{
    private const string PayPalHttpClientName = "PayPal";

    /// <summary>
    /// Registers the PayPal integration runtime: a long-lived <see cref="PayPalClient"/> over an isolated
    /// named <see cref="HttpClient"/> (explicit logger, request-body logging off so card data never reaches
    /// logs), and the <see cref="IPayPalPaymentGateway"/> boundary. Binding and startup fail-fast validation
    /// of <see cref="PayPalSettings"/> are performed by the host (Program.cs), which has the hosting layer.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services)
    {
        // Isolated HttpClient: per-attempt hard timeout + connection recycling for a long-lived client.
        services.AddHttpClient(PayPalHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new PayPalClientOptions
            {
                // The SDK exposes a single environment (Production) that already targets PayPal sandbox;
                // a live deployment supplies PayPal:BaseUrl to point elsewhere (see below).
                Environment = ServerEnvironment.Production,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                // Per-attempt bound (whole-call budget is enforced in the gateway via a linked token).
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
                // Explicit logger disarms the PAYPALCLIENT_LOG env var; request bodies (card data) never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional override: when set, used verbatim for every call including the OAuth token request.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new PayPalClient(httpClient, options);
        });

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
