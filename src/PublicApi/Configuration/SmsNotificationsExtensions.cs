using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the SMS order-notification feature: the <c>Twilio:</c> settings, the HTTP-based provider,
/// and the two application services that sit on top of it.
/// </summary>
public static class SmsNotificationsExtensions
{
    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ISmsProvider, TwilioSmsProvider>(client =>
        {
            // Matches Twilio's own SDK default; list/reconciliation calls can take tens of seconds
            // on a busy account, so a short timeout would spuriously fail otherwise-successful calls.
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
