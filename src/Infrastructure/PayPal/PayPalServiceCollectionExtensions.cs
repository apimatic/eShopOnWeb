using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal gateway and the order-payment, saved-card and reconciliation services.
    /// Binds settings from the <c>PayPal:</c> configuration section.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();

        // Typed HttpClient whose base address is the resolved PayPal API endpoint (override or environment-derived).
        services.AddHttpClient<IPayPalPaymentGateway, PayPalPaymentGateway>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<PayPalSettings>>().Value;
            client.BaseAddress = new Uri(settings.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
