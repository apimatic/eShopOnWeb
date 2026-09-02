using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.CONFIG_NAME).Bind(settings);

        // Fall back to the PAYPAL_* environment variables when the PayPal: section is not populated
        // (e.g. user-secrets were not set up); the section keys remain the binding contract.
        settings.ClientId = FirstNonEmpty(settings.ClientId, Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID"));
        settings.ClientSecret = FirstNonEmpty(settings.ClientSecret, Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET"));
        settings.Environment = FirstNonEmpty(settings.Environment, Environment.GetEnvironmentVariable("PAYPAL_ENVIRONMENT"));
        if (string.IsNullOrWhiteSpace(settings.Environment))
        {
            settings.Environment = "sandbox";
        }
        settings.Currency = FirstNonEmpty(settings.Currency, Environment.GetEnvironmentVariable("PAYPAL_CURRENCY"));
        if (string.IsNullOrWhiteSpace(settings.Currency))
        {
            settings.Currency = "USD";
        }

        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, c =>
        {
            // Bounds one attempt; the SDK's own per-attempt timeout sits on top.
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            // Optional override: used verbatim as the API base address for every PayPal call,
            // including the OAuth token request (the SDK resolves both through this value).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        return services;
    }

    private static string FirstNonEmpty(string? configured, string? fallback)
        => string.IsNullOrWhiteSpace(configured) ? (fallback ?? string.Empty) : configured;
}
