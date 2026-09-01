using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalClientRegistration
{
    /// <summary>Named HttpClient so PayPal's pipeline (timeout, handlers) stays off the shared default client.</summary>
    public const string HttpClientName = "PayPal";

    /// <summary>
    /// Binds the "PayPal" configuration section, validates it, and registers the long-lived
    /// SDK client (singleton over an IHttpClientFactory-managed named client) plus the gateway.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>()
            ?? new PayPalSettings();
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop; bounds a hung provider. The gateway adds a total call budget.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Validated lazily so hosts that never call PayPal (e.g. test hosts) still boot.
            Validate(settings);
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId!,
                    ClientSecret = settings.ClientSecret!
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
                Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        // The SDK models only the Sandbox server node; environment selection and the
                        // optional BaseUrl override both resolve to this URL, which also governs the
                        // OAuth token request.
                        Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = ResolveBaseUrl(settings) }
                    }
                }
            };
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalGateway>();
        return services;
    }

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl;
        }

        return string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    private static void Validate(PayPalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId is not configured. Provide it via user-secrets or the PAYPAL_CLIENT_ID environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientSecret is not configured. Provide it via user-secrets or the PAYPAL_CLIENT_SECRET environment variable.");
        }
        if (string.IsNullOrWhiteSpace(settings.Currency))
        {
            throw new InvalidOperationException(
                "PayPal:Currency is not configured. Provide it via user-secrets or the PAYPAL_CURRENCY environment variable.");
        }
        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{settings.Environment}' is not supported; use 'sandbox' or 'production'.");
        }
    }
}
