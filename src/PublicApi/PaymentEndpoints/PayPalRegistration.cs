using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PayPalRegistration
{
    /// <summary>
    /// Registers the PayPal-backed payment gateway and the application services that orchestrate
    /// the pay / fulfil / cancel / refund / reconcile and saved-card flows. Settings are bound from
    /// the <c>PayPal:</c> configuration section; no values are hard-coded.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        var config = configuration.GetSection(PayPalConfiguration.SectionName).Get<PayPalConfiguration>()
                     ?? new PayPalConfiguration();
        services.AddSingleton(config);
        services.Configure<PayPalConfiguration>(configuration.GetSection(PayPalConfiguration.SectionName));

        services.AddHttpClient<IPaymentGateway, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
