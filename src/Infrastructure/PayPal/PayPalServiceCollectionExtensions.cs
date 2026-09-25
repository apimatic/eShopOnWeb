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
    /// Registers the PayPal SDK client (as a long-lived singleton over a named HttpClient) and the
    /// <see cref="IPaymentGateway"/>. Fails fast at startup if a credential is missing or blank, so a
    /// misconfiguration surfaces here rather than as a 401 on the first call.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);

        // Eager, fail-fast validation — runs during host build, before the app serves anything.
        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        ValidateSettings(settings);

        services.AddHttpClient(HttpClientName, c =>
            {
                // Per-attempt backstop for a hung socket; the whole-call budget lives in the gateway.
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keep DNS fresh behind the long-lived singleton client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var current = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            if (!string.IsNullOrWhiteSpace(current.Environment) &&
                !string.Equals(current.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
            {
                loggerFactory.CreateLogger("PayPal")
                    .LogWarning("PayPal:Environment '{Environment}' is not 'sandbox'; this SDK build targets the PayPal sandbox only.",
                        current.Environment);
            }

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = current.ClientId,
                    ClientSecret = current.ClientSecret
                },
                // Per-attempt timeout; POST/DELETE are never auto-retried by the SDK.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) },
                Logging = new LoggingOptions
                {
                    // Assigned explicitly so the PAYPALSERVERSDKCLIENT_LOG env var cannot switch on
                    // request-body logging (which would print card PANs unredacted).
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional base-URL override, applied verbatim to every call including the token request
            // (the token URL is resolved through this same server config).
            if (!string.IsNullOrWhiteSpace(current.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = current.BaseUrl!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentConfiguration, PaymentConfiguration>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        return services;
    }

    private static void ValidateSettings(PayPalSettings s)
    {
        // Each credential part checked separately — a blank part is not a missing one.
        if (string.IsNullOrWhiteSpace(s.ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured. Set it via user-secrets or an environment variable before starting the app.");
        if (string.IsNullOrWhiteSpace(s.ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured. Set it via user-secrets or an environment variable before starting the app.");
        if (string.IsNullOrWhiteSpace(s.Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured. Set it via user-secrets or an environment variable before starting the app.");
        if (s.Currency.Trim().Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a 3-letter ISO-4217 currency code.");
    }
}
