using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Wires up the PayPal integration: the SDK client (over a dedicated, named <see cref="HttpClient"/>
/// so its timeout and handler lifetime stay scoped to PayPal), the gateway, and the payment
/// application services.
/// </summary>
public static class PayPalRegistration
{
    private const string HttpClientName = "PayPal";
    private const string ProductionBaseUrl = "https://api-m.paypal.com";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, PayPalSettings settings)
    {
        // Registered as a concrete singleton (same pattern as CatalogSettings) so ApplicationCore
        // services can consume it without an Options dependency.
        services.AddSingleton(settings);

        // A named HttpClient keeps the timeout + primary-handler lifetime scoped to PayPal, and
        // PooledConnectionLifetime keeps DNS fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return BuildClient(httpClient, settings);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static PayPalServerSdkClient BuildClient(HttpClient httpClient, PayPalSettings settings)
    {
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? string.Empty,
                ClientSecret = settings.ClientSecret ?? string.Empty
            },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) }
        };

        // Environment selection == base-URL selection: the SDK exposes only a Sandbox environment,
        // so an explicit override (or a production environment) is applied by overriding the base URL,
        // which the SDK also uses for the OAuth2 token request.
        var baseUrl = ResolveBaseUrl(settings);
        if (!string.IsNullOrWhiteSpace(baseUrl))
            options.Server.Default.Sandbox.BaseUrl = baseUrl;

        return new PayPalServerSdkClient(httpClient, options);
    }

    private static string? ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            return settings.BaseUrl.Trim();          // explicit override — used verbatim for every call
        if (settings.IsProduction)
            return ProductionBaseUrl;
        return null;                                  // sandbox default
    }
}
