using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the PayPal-backed payment stack: settings bound from the <c>PayPal:</c> section,
/// the HTTP gateway, and the application services that orchestrate orders, payments and saved cards.
/// </summary>
public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);
        services.AddSingleton<IPaymentSettings>(settings);

        services.AddMemoryCache();

        // Typed HttpClient: the gateway receives an HttpClient; everything else is resolved from DI.
        services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
