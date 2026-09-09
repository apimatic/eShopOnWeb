using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="PayPalSettings"/> from the <c>PayPal:</c> configuration section and wires
    /// the hand-written PayPal client (token provider + typed HttpClient) together with the
    /// payment/saved-card/reconciliation services. The API base URL follows
    /// <see cref="PayPalSettings.GetApiBaseUrl"/> (honouring an explicit <c>PayPal:BaseUrl</c>).
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        var baseUrl = settings.GetApiBaseUrl();

        services.AddHttpClient(PayPalTokenProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
        });

        services.AddSingleton<PayPalTokenProvider>();

        services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
        });

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
