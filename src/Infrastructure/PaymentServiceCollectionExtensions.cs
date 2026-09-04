using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class PaymentServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        var payPalOptions = configuration.GetSection(PayPalOptions.CONFIG_NAME).Get<PayPalOptions>() ?? new PayPalOptions();
        services.AddSingleton(payPalOptions);

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            return PayPalServerClientFactory.Create(payPalOptions, httpClient);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }
}