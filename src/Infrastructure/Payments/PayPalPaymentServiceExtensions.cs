using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Registers the PayPal-backed payment integration: transport settings, the typed HTTP
/// client for the gateway, and the application services that orchestrate the flows.
/// </summary>
public static class PayPalPaymentServiceExtensions
{
    public static IServiceCollection AddPayPalPaymentServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Bind transport settings from the PayPal: section (values come from configuration /
        // user-secrets — never hard-coded here).
        var section = configuration.GetSection(PayPalSettings.SectionName);
        var settings = new PayPalSettings
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? "sandbox",
            Currency = section["Currency"] ?? "USD",
            BaseUrl = section["BaseUrl"]
        };

        services.AddSingleton(settings);
        services.AddSingleton(new PaymentSettings { Currency = settings.Currency });

        var baseUrl = settings.ResolveBaseUrl();
        services.AddHttpClient<IPayPalGateway, PayPalGateway>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
