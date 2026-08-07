using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers the PayPal payment integration: settings binding, the HTTP client (base address
/// resolved from configuration), the OAuth token provider, the spec-driven gateway and the
/// order-payment / saved-card application services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPaymentProcessing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.ConfigSection));

        services.AddHttpClient(PayPalHttpClient.Name, (serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<PayPalSettings>>().Value;
            // Trailing slash so relative request paths (e.g. "v2/checkout/orders") combine correctly.
            client.BaseAddress = new Uri(settings.ResolveBaseUrl() + "/");
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton<PayPalAccessTokenProvider>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
