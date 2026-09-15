using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public static class PayPalConfiguration
{
    public static IConfigurationBuilder AddPayPalEnvironmentOverrides(this IConfigurationBuilder builder)
    {
        var map = new Dictionary<string, string?>();
        Map(map, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Map(map, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Map(map, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Map(map, "PAYPAL_CURRENCY", "PayPal:Currency");
        Map(map, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (map.Count > 0)
        {
            builder.AddInMemoryCollection(map);
        }

        return builder;
    }

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalOptions>>().Value);
        services.AddSingleton<PayPalAccessTokenCache>();
        services.AddHttpClient<IPayPalPaymentsClient, PayPalPaymentsClient>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        return services;
    }

    private static void Map(IDictionary<string, string?> map, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            map[configurationKey] = value;
        }
    }
}
