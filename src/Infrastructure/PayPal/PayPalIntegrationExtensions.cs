using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalIntegrationExtensions
{
    /// <summary>
    /// Registers the PayPal client + gateway as long-lived singletons (one HttpClient
    /// pipeline for the process, so the OAuth token cache is shared and rotated properly).
    /// Settings bind from the "PayPal:" configuration section; values fall back to the
    /// PAYPAL_* environment variables. Nothing is hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = ResolveSettings(configuration);
        services.AddSingleton(settings);

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal is not configured: set PayPal:ClientId and PayPal:ClientSecret (via configuration, " +
                "user-secrets, or the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }

        var currency = settings.Currency?.Trim();
        if (string.IsNullOrEmpty(currency) || currency.Length != 3)
        {
            throw new InvalidOperationException("PayPal is not configured: PayPal:Currency must be an ISO-4217 three-letter code.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            // v1.0.1 exposes only the Sandbox server environment; live is reached solely via
            // the BaseUrl override below.
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            },
            // All RetryOptions members are required — build from Default() and adjust.
            // Per-attempt bound; the gateway adds the whole-call budget. Transport failures
            // ARE retried on every verb, which is what the SingleSendGuardHandler defends.
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(15)
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Every call — including the OAuth token request — resolves through
            // Server.Default(...), so one verbatim override covers the whole client.
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }
        else if (!settings.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PayPal:Environment is not 'sandbox', but this SDK build only exposes the sandbox server; " +
                "set PayPal:BaseUrl to the intended API address to target another environment.");
        }

        var httpClient = new HttpClient(new SingleSendGuardHandler
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        services.AddSingleton(httpClient);
        services.AddSingleton(new PayPalServerSdkClient(httpClient, options));
        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }

    private static PayPalSettings ResolveSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SECTION_NAME);
        return new PayPalSettings
        {
            ClientId = NonBlank(section[nameof(PayPalSettings.ClientId)]) ?? ReadEnv("PAYPAL_CLIENT_ID"),
            ClientSecret = NonBlank(section[nameof(PayPalSettings.ClientSecret)]) ?? ReadEnv("PAYPAL_CLIENT_SECRET"),
            Environment = NonBlank(section[nameof(PayPalSettings.Environment)]) ?? NonBlank(ReadEnv("PAYPAL_ENVIRONMENT")) ?? "sandbox",
            Currency = NonBlank(section[nameof(PayPalSettings.Currency)]) ?? NonBlank(ReadEnv("PAYPAL_CURRENCY")) ?? "USD",
            BaseUrl = NonBlank(section[nameof(PayPalSettings.BaseUrl)]) ?? NonBlank(ReadEnv("PAYPAL_BASE_URL")) ?? string.Empty
        };
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ReadEnv(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
}
