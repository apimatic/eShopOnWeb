using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class OrderNotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SMS order-notification capability: Twilio settings bound from the <c>Twilio:</c>
    /// section, the spec-driven Twilio gateway (as a typed <see cref="System.Net.Http.HttpClient"/>),
    /// and the application orchestration service.
    /// </summary>
    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        // Business policy (provider-agnostic) bound from the same Twilio: section.
        var notificationSettings = configuration.GetSection(TwilioSettings.SectionName).Get<NotificationSettings>() ?? new NotificationSettings();
        services.AddSingleton(notificationSettings);

        services.AddHttpClient<IOrderNotificationGateway, TwilioNotificationGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "eShopOnWeb-Notifications");
        });

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
