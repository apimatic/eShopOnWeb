using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal integration: settings bound from the <c>PayPal:</c> section, the
    /// HTTP transport, the gateway adapter, and the order-payment / saved-card services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalOptions.SectionName);
        var options = new PayPalOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? "sandbox",
            Currency = section["Currency"] ?? "USD",
            BaseUrl = section["BaseUrl"]
        };

        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IPaymentSettings>(options);

        services.AddSingleton<PayPalHttpClient>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();

        return services;
    }
}
