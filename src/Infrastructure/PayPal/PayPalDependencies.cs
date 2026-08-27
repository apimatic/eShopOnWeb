using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalDependencies
{
    /// <summary>
    /// Registers the PayPal gateway (typed HttpClient) and the payment orchestration services.
    /// Settings bind from the "PayPal" configuration section; secrets come from user-secrets
    /// or environment variables, never from files in the repository.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));

        services.AddHttpClient<IPaymentGateway, PayPalClient>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();

        return services;
    }
}
