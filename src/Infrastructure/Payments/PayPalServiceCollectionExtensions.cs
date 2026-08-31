using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Binds the PayPal: configuration section (ClientId, ClientSecret, Environment,
    /// Currency, optional BaseUrl) and registers the spec-based PayPal client plus the
    /// payment orchestration services.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>()
            .Bind(configuration.GetSection(PayPalOptions.SectionName));

        services.AddHttpClient<IPayPalClient, PayPalClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            client.BaseAddress = new Uri(options.ResolveBaseUrl());
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, SavedPaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
