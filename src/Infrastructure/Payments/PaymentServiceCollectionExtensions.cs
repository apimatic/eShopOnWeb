using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// Wires the PayPal-backed payment stack: the SDK client (bound from the "PayPal"
    /// configuration section), the gateway, and the order-payment / saved-card /
    /// reconciliation services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new PayPalOptions();
        configuration.GetSection(PayPalOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (via user-secrets or environment).");
        }

        var currency = string.IsNullOrWhiteSpace(options.Currency) ? "USD" : options.Currency!.Trim().ToUpperInvariant();
        services.AddSingleton(new PaymentSettings { Currency = currency });

        var baseUrl = ResolveBaseUrl(options);

        services.AddSingleton(_ =>
        {
            // A single, long-lived client and HttpClient, per the SDK guidance. The pooled
            // connection lifetime keeps DNS fresh behind the singleton.
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            var clientOptions = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = options.ClientId!,
                    ClientSecret = options.ClientSecret!
                }
            };
            // Used verbatim for EVERY call, including the OAuth token request.
            clientOptions.Server.Default.Sandbox.BaseUrl = baseUrl;

            return new PayPalServerSdkClient(httpClient, clientOptions);
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl!.Trim();
        }

        return (options.Environment?.Trim().ToLowerInvariant()) switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl
        };
    }
}
