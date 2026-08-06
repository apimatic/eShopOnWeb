using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal payments + saved-cards feature: settings binding, the configured
    /// PayPal HttpClient, the OAuth token provider, the gateway, and the application services
    /// that orchestrate ordering, paying, refunding and card vaulting.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddHttpClient(PayPalHttpClient.Name, client =>
        {
            client.BaseAddress = new Uri(settings.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton<IPayPalAccessTokenProvider, PayPalAccessTokenProvider>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();

        // Application services that drive the two flows.
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
