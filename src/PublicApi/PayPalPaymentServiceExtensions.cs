using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Registers the PayPal gateway and the additive payment/saved-card application services. Settings are
/// bound from the <c>PayPal:</c> configuration section (supplied via user-secrets from environment
/// variables); no values are hard-coded here.
/// </summary>
public static class PayPalPaymentServiceExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Typed HTTP client for the PayPal REST APIs. The OAuth token is cached across instances.
        services.AddHttpClient<IPaymentGateway, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
