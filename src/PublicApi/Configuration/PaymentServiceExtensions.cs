using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the PayPal-backed payment capabilities: settings binding, the typed PayPal HTTP client,
/// and the application services that drive the pay/fulfil/cancel/refund and saved-card flows.
/// </summary>
public static class PaymentServiceExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
