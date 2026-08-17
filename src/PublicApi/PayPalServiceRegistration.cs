using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Wires up the additive PayPal payment capability: settings bound from the <c>PayPal:</c> section,
/// the typed PayPal REST client, and the order-payment / saved-card / reconciliation services.
/// </summary>
public static class PayPalServiceRegistration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();
        // Note: credentials are validated lazily in PayPalClient's constructor (only when a payment
        // endpoint is actually exercised), so the app and its functional tests still boot without
        // PayPal configuration present.

        // Consumed directly by the services and the client.
        services.AddSingleton(settings);

        var baseUrl = settings.ResolveBaseUrl();
        services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
