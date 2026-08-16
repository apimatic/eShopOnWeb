using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the PayPal integration: settings bound from the <c>PayPal:</c> section, the REST gateway
/// (a typed <see cref="System.Net.Http.HttpClient"/>), a singleton token cache, and the payment
/// application services.
/// </summary>
public static class PayPalConfiguration
{
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        services.AddHttpContextAccessor();

        services.AddSingleton<PayPalTokenStore>();
        services.AddHttpClient<IPayPalGateway, PayPalGateway>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
