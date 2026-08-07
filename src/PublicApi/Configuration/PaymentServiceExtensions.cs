using System;
using System.Net.Http.Headers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class PaymentServiceExtensions
{
    /// <summary>
    /// Registers the PayPal-backed payment gateway: binds <see cref="PayPalSettings"/> from the
    /// "PayPal" configuration section, configures a named HttpClient pointed at the resolved base
    /// address, and wires the token provider and gateway service.
    /// </summary>
    public static IServiceCollection AddPayPalPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        services.Configure<PayPalSettings>(section);

        var settings = section.Get<PayPalSettings>() ?? new PayPalSettings();
        var baseAddress = new Uri(settings.ResolveBaseUrl().TrimEnd('/') + "/");

        services.AddHttpClient(PayPalConstants.HttpClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddSingleton<PayPalAccessTokenProvider>();
        services.AddScoped<IPaymentGatewayService, PayPalPaymentGatewayService>();

        return services;
    }
}
