using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentsRegistration
{
    /// <summary>
    /// Wires up PayPal payments: settings from the "PayPal" section, the spec-built
    /// PayPal client, the gateway, and the order-payment / saved-card services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is required (set the PAYPAL_CLIENT_ID environment variable or user-secret).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is required (set the PAYPAL_CLIENT_SECRET environment variable or user-secret).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency is required (set the PAYPAL_CURRENCY environment variable or user-secret).")
            .ValidateOnStart();

        services.AddHttpClient<PayPalClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
