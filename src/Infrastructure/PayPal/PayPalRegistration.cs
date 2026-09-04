using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalRegistration
{
    /// <summary>
    /// Registers the PayPal settings (bound from the "PayPal" configuration section),
    /// a dedicated named HttpClient for the SDK, and the gateway as a singleton.
    /// </summary>
    public static IServiceCollection AddPayPalGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddHttpClient(PayPalGateway.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton<IPayPalGateway, PayPalGateway>();
        return services;
    }
}
