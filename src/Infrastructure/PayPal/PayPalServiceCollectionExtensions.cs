using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Wires the PayPal integration and the payment orchestration services. All settings are bound from
/// the <c>PayPal:</c> configuration section; nothing is hard-coded.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalOptions>(configuration.GetSection(PayPalOptions.SectionName));

        // A single named HttpClient whose base address honours PayPal:BaseUrl (or is derived from the
        // environment). Both the token provider and the API client use it, so the override applies to
        // every call including the credential/token request.
        services.AddHttpClient(PayPalClient.HttpClientName, (sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
            http.BaseAddress = new Uri(options.ResolveBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton<PayPalTokenProvider>();
        services.AddScoped<PayPalClient>();
        services.AddScoped<IPayPalPaymentGateway>(sp => sp.GetRequiredService<PayPalClient>());
        services.AddScoped<IPayPalVault>(sp => sp.GetRequiredService<PayPalClient>());
        services.AddScoped<IPayPalReconciliation>(sp => sp.GetRequiredService<PayPalClient>());

        services.AddSingleton<IPaymentConcurrencyGuard, KeyedPaymentConcurrencyGuard>();
        services.AddSingleton<IPaymentSettings>(sp => sp.GetRequiredService<IOptions<PayPalOptions>>().Value);

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
