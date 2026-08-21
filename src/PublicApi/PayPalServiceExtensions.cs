using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalServiceExtensions
{
    public static ConfigurationManager ApplyPayPalEnvironmentVariables(this ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>();
        Copy(configuration, overrides, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy(configuration, overrides, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy(configuration, overrides, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy(configuration, overrides, "PAYPAL_CURRENCY", "PayPal:Currency");
        Copy(configuration, overrides, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (overrides.Count > 0)
        {
            configuration.AddInMemoryCollection(overrides);
        }

        return configuration;
    }

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton<IPaymentSettings, PayPalPaymentSettings>();
        services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        return services;
    }

    private static void Copy(
        IConfiguration configuration,
        IDictionary<string, string?> overrides,
        string environmentVariable,
        string configurationKey)
    {
        var value = configuration[environmentVariable] ?? System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configurationKey] = value;
        }
    }
}
