using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal integration: settings bound from the <c>PayPal:</c> configuration
    /// section, the typed HTTP client, and the gateway implementations.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddHttpClient<PayPalApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddTransient<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddTransient<IPayPalVaultGateway, PayPalVaultGateway>();
        services.AddTransient<IPayPalReportingGateway, PayPalReportingGateway>();
        services.AddSingleton<IPaymentCurrencyProvider, PayPalCurrencyProvider>();

        return services;
    }
}
