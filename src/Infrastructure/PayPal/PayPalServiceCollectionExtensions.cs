using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Wires the PayPal-backed payment integration: settings binding, the hand-written REST
    /// client (as gateway / vault / reporting), the orchestration services and the idempotency
    /// lock. All PayPal calls go to <see cref="PayPalSettings.ResolveBaseUrl"/> (honouring the
    /// optional PayPal:BaseUrl override).
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));
        services.AddSingleton<IPaymentConfiguration>(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);

        services.AddHttpClient(PayPalClient.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            client.BaseAddress = new Uri(settings.ResolveBaseUrl() + "/");
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton<PayPalTokenProvider>();
        services.AddSingleton<PayPalClient>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<PayPalClient>());
        services.AddSingleton<ICardVault>(sp => sp.GetRequiredService<PayPalClient>());
        services.AddSingleton<ITransactionReporting>(sp => sp.GetRequiredService<PayPalClient>());

        services.AddSingleton<IPaymentLock, KeyedPaymentLock>();

        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        return services;
    }
}
