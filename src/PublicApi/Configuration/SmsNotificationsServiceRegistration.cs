using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class SmsNotificationsServiceRegistration
{
    /// <summary>
    /// Registers the SMS order-notification feature: Twilio settings (bound from the <c>Twilio:</c> section),
    /// the Twilio-backed <see cref="ISmsGateway"/> as a typed HttpClient, and the application services that
    /// drive the flows.
    /// </summary>
    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationOperationsService, NotificationOperationsService>();

        return services;
    }
}
