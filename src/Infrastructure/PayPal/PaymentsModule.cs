using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires up the PayPal payment integration: settings (from the <c>PayPal:</c> section, exact keys), the
/// gateway (a singleton so its OAuth token is cached across requests), and the application services.
/// </summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind exactly the documented keys; never hard-code the values.
        var settings = new PayPalSettings
        {
            ClientId = configuration["PayPal:ClientId"] ?? string.Empty,
            ClientSecret = configuration["PayPal:ClientSecret"] ?? string.Empty,
            Environment = configuration["PayPal:Environment"] ?? "sandbox",
            Currency = configuration["PayPal:Currency"] ?? "USD",
            BaseUrl = configuration["PayPal:BaseUrl"]
        };

        services.AddSingleton(settings);
        services.AddSingleton<IPaymentSettings>(settings);

        services.AddHttpClient(PayPalClient.HttpClientName);
        services.AddSingleton<IPayPalGateway, PayPalClient>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
