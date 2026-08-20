using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ITwilioLookupClient, TwilioLookupClient>(client =>
            {
                client.BaseAddress = new Uri("https://lookups.twilio.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .RemoveAllLoggers();

        services.AddHttpClient<ITwilioMessagingClient, TwilioMessagingClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .RemoveAllLoggers();

        services.AddSingleton<IMessagingProviderSettings, TwilioMessagingProviderSettings>();
        services.AddSingleton<INotificationRedactionState, NotificationRedactionState>();
        services.AddScoped<ITrackedNotificationStore, TrackedNotificationStore>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IShopperOrderService, ShopperOrderService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
