using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Wires up the SMS order-notification feature: the Twilio SDK client, the gateway that adapts it to
/// the application's port, the settings bound from configuration, and the application services.
/// </summary>
public static class OrderNotificationServiceExtensions
{
    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind provider config strictly from the Twilio: section (values come from user-secrets / env).
        var twilioSection = configuration.GetSection(TwilioSettings.SectionName);
        services.Configure<TwilioSettings>(twilioSection);
        var twilioSettings = twilioSection.Get<TwilioSettings>() ?? new TwilioSettings();

        // Application-level notification knobs (optional section, sensible defaults).
        var notificationSettings = configuration.GetSection(NotificationSettings.SectionName).Get<NotificationSettings>()
            ?? new NotificationSettings();
        services.AddSingleton<INotificationSettings>(notificationSettings);

        // Register the Twilio SDK client. Auth uses the account SID + auth token (Basic). The optional
        // Twilio:BaseUrl overrides the messaging API base address for every messaging-API call.
        services.AddTwilioSdkClient(options =>
        {
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = twilioSettings.AccountSid,
                Password = twilioSettings.AuthToken
            };

            if (!string.IsNullOrWhiteSpace(twilioSettings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = twilioSettings.BaseUrl;
            }
        });

        services.AddScoped<INotificationGateway, TwilioNotificationGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderMessagingService, OrderMessagingService>();

        return services;
    }
}
