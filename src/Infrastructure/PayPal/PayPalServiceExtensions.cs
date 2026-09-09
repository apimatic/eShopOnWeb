using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceExtensions
{
    /// <summary>
    /// Registers the PayPal integration: settings bound from the <c>PayPal:</c> section, the typed
    /// HTTP client pointed at the configured environment (or <c>PayPal:BaseUrl</c> override), and the
    /// order-payment, saved-card and reconciliation services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddSingleton(settings);
        services.AddSingleton<IPayPalConfiguration>(settings);
        services.AddSingleton<PayPalTokenCache>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddSingleton<PaymentReferenceFactory>();

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
