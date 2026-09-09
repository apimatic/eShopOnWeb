using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the additive PayPal payment integration: options bound from the "PayPal" section,
/// the gateway as a typed HttpClient built to PayPal's spec, and the orchestration service.
/// </summary>
public static class PaymentServicesExtensions
{
    public static IServiceCollection AddPayPalPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton<IPaymentSettings>(sp => sp.GetRequiredService<IOptions<PayPalOptions>>().Value);

        services.AddHttpClient<IPayPalPaymentGateway, PayPalPaymentGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }
}
