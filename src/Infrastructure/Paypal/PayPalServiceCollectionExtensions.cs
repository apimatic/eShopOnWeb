using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Paypal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal gateway and the payment/saved-card/reconciliation application services.
    /// The <paramref name="options"/> are bound from the <c>PayPal:</c> configuration section by the host.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, PayPalOptions options)
    {
        options.Validate();
        services.AddSingleton(options);

        // Every PayPal call (including the token request) goes to this base address; when PayPal:BaseUrl
        // is set it is used verbatim, otherwise it is derived from PayPal:Environment.
        services.AddHttpClient(PayPalTokenProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton<PayPalTokenProvider>();
        services.AddScoped<IPayPalPaymentGateway, PayPalClient>();

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
