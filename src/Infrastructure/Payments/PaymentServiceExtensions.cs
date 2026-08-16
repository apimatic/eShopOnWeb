using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceExtensions
{
    /// <summary>
    /// Registers the PayPal integration: settings bound from the "PayPal:" configuration
    /// section, HTTP clients pointed at the resolved base URL, the token provider, the
    /// gateway, and the application services that orchestrate the payment flows.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        var baseAddress = new Uri(settings.ResolveBaseUrl());

        // Named client used by the token provider (client-credentials flow).
        services.AddHttpClient(PayPalAccessTokenProvider.HttpClientName, client =>
        {
            client.BaseAddress = baseAddress;
        });

        services.AddSingleton<IPayPalAccessTokenProvider, PayPalAccessTokenProvider>();

        // Typed client for all PayPal REST calls.
        services.AddHttpClient<IPayPalPaymentGateway, PayPalPaymentGateway>(client =>
        {
            client.BaseAddress = baseAddress;
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
