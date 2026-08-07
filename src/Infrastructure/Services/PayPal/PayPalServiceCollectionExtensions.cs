using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal payment gateway and its supporting services. Binds <see cref="PayPalSettings"/>
    /// from the "PayPal" configuration section and validates that credentials are present.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var settingsSection = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(settingsSection);

        var settings = settingsSection.Get<PayPalSettings>() ?? new PayPalSettings();
        settings.Validate();

        var baseUrl = settings.ResolveBaseUrl();
        services.AddHttpClient(PayPalHttpClient.Name, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<IPayPalAccessTokenProvider, PayPalAccessTokenProvider>();
        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();

        return services;
    }
}
