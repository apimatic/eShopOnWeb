using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal integration: settings bound from the <c>PayPal:</c> section, the typed
    /// HttpClient whose base address is the verbatim <c>PayPal:BaseUrl</c> override (else derived from
    /// <c>PayPal:Environment</c>), the token provider + low-level client, the three gateways, and the
    /// application-layer payment/saved-card/reconciliation services.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        var baseUrl = settings.ResolveBaseUrl();

        services.AddHttpClient(PayPalHttpClientNames.Api, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        // Token caching is process-wide, so the provider is a singleton.
        services.AddSingleton<PayPalTokenProvider>();
        services.AddScoped<PayPalApiClient>();

        services.AddScoped<IPayPalPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPayPalVaultGateway, PayPalVaultGateway>();
        services.AddScoped<IPayPalReportingGateway, PayPalReportingGateway>();
        services.AddSingleton<IPaymentCurrencyProvider, PayPalCurrencyProvider>();

        // Application-layer orchestration.
        services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
