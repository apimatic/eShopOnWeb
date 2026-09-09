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
    /// Registers the PayPal integration: settings bound from the <c>PayPal:</c> section,
    /// the typed PayPal HTTP client, and the payment/saved-card/reconciliation services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency must be set.");

        services.AddSingleton<IPaymentConfiguration>(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        services.AddMemoryCache();

        services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
