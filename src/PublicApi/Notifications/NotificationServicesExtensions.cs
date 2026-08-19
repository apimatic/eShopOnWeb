using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Notifications.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Wires up the SMS order-notification feature: the Twilio gateway (a typed HttpClient built to
/// the OpenAPI specs), its options bound from the <c>Twilio:</c> section, and the application
/// services that drive the flows.
/// </summary>
public static class NotificationServicesExtensions
{
    public static IServiceCollection AddOrderSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        // The gateway is the only thing that talks to Twilio; it is a typed HttpClient with a
        // sensible network timeout so a slow provider never hangs an order operation indefinitely.
        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
