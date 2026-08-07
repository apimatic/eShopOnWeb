using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PayPalRegistration
{
    /// <summary>
    /// Registers the PayPal payment gateway (settings bound from the <c>PayPal:</c> section, a typed
    /// HttpClient pointed at the resolved API base address, a shared access-token cache) plus the
    /// per-order lock used to keep payment operations idempotent.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        var baseUrl = settings.ResolveBaseUrl();

        services.AddSingleton<PayPalAccessTokenCache>();
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IPayPalPaymentGateway, PayPalPaymentGateway>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
