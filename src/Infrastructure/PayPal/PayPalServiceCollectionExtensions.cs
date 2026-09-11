using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires up the PayPal payment integration: settings binding, the OAuth token provider, the typed
/// HTTP client, and the payment application services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind settings from the PayPal: section (values come from user-secrets / environment).
        var section = configuration.GetSection(PayPalOptions.SectionName);
        var options = new PayPalOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? "sandbox",
            Currency = section["Currency"] ?? "USD",
            BaseUrl = section["BaseUrl"]
        };
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<ICurrencyProvider, PayPalCurrencyProvider>();
        services.AddSingleton<PayPalTokenProvider>();

        // A bare named client for the token request, and the typed client for API calls.
        services.AddHttpClient(PayPalHttp.TokenClientName);
        services.AddHttpClient<IPaymentGateway, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        // Payment application services.
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
