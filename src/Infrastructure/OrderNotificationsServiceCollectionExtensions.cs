using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

/// <summary>
/// Wires up the SMS order-notification capability: provider settings (bound from the <c>Twilio:</c> section),
/// the two provider gateways as typed HTTP clients, and the application services.
/// </summary>
public static class OrderNotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.ConfigurationSection));

        // The messaging gateway — sends, reads, cancels, redacts and lists messages. Base host honours
        // Twilio:BaseUrl when set (resolved per request inside the client).
        services.AddHttpClient<ISmsProvider, TwilioMessagingClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // The lookup gateway — validates and canonicalises numbers. Served from its own host.
        services.AddHttpClient<IPhoneNumberLookup, TwilioPhoneNumberLookup>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
