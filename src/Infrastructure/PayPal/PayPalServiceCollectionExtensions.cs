using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the PayPal integration: binds settings from the "PayPal:" section (with PAYPAL_* env fallback),
    /// registers the OAuth token provider and typed HTTP client, the gateway, and the payment/saved-card/
    /// reconciliation application services. Secrets are read from configuration, never hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var options = BuildOptions(configuration);
        var baseAddress = new Uri(options.ResolveBaseUrl());

        services.AddSingleton(options);
        services.AddSingleton<IPaymentConfiguration, PayPalConfiguration>();
        services.AddSingleton<IPayPalTokenProvider, PayPalTokenProvider>();

        // Named client for the OAuth token request (Basic-auth); base address honors PayPal:BaseUrl verbatim.
        services.AddHttpClient(PayPalTokenProvider.HttpClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Typed client for the PayPal REST APIs; the gateway attaches the bearer token itself.
        services.AddHttpClient<IPayPalGateway, PayPalGateway>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }

    private static PayPalOptions BuildOptions(IConfiguration configuration)
    {
        // Configuration (user-secrets / appsettings) takes precedence; PAYPAL_* environment variables are the
        // fallback so the integration also runs headless where user-secrets are not loaded. Values are never
        // written into the repository.
        return new PayPalOptions
        {
            ClientId = Coalesce(configuration["PayPal:ClientId"], Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID")),
            ClientSecret = Coalesce(configuration["PayPal:ClientSecret"], Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET")),
            Environment = Coalesce(configuration["PayPal:Environment"], Environment.GetEnvironmentVariable("PAYPAL_ENVIRONMENT")),
            Currency = Coalesce(configuration["PayPal:Currency"], Environment.GetEnvironmentVariable("PAYPAL_CURRENCY")),
            BaseUrl = Coalesce(configuration["PayPal:BaseUrl"], Environment.GetEnvironmentVariable("PAYPAL_BASEURL"))
        };
    }

    private static string? Coalesce(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : (!string.IsNullOrWhiteSpace(fallback) ? fallback : null);
}
