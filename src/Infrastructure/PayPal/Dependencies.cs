using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class Dependencies
{
    /// <summary>
    /// Registers the PayPal payment gateway and the payment/saved-card/reconciliation services.
    /// Binds the "PayPal" configuration section (PayPal:ClientId, PayPal:ClientSecret,
    /// PayPal:Environment, PayPal:Currency, PayPal:BaseUrl).
    /// </summary>
    public static void ConfigurePayPalServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));
        // PaymentOptions.Currency binds from PayPal:Currency.
        services.Configure<PaymentOptions>(configuration.GetSection(PayPalSettings.CONFIG_NAME));

        services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
    }
}
