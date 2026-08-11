using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the PayPal gateway and the payment/saved-card application services, binding
/// <see cref="PayPalSettings"/> from the "PayPal:" configuration section. Nothing here is
/// hard-coded — every value comes from configuration/user-secrets/environment.
/// </summary>
public static class PayPalServiceRegistration
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PayPalSettings.SectionName).Get<PayPalSettings>()
                       ?? new PayPalSettings();
        services.AddSingleton(settings);

        // BaseUrl (when set) is used verbatim as the API base for every PayPal call — including the
        // token request — otherwise it is derived from PayPal:Environment.
        services.AddHttpClient(PayPalGateway.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(settings.ResolveBaseUrl());
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddSingleton<PayPalTokenProvider>();
        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();

        return services;
    }
}
