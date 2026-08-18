using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Wires up the order SMS-notification capability: provider settings, the HTTP clients that talk to
/// the messaging and lookup APIs, and the application services that orchestrate them.
/// </summary>
public static class OrderSmsNotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddOrderSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.Configure<SmsNotificationOptions>(configuration.GetSection(SmsNotificationOptions.SectionName));

        // Typed HTTP clients: one for the messaging API (base-URL override honoured inside the
        // client), one for the lookup API (its own host).
        services.AddHttpClient<ITwilioMessagingClient, TwilioMessagingClient>();
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<ISmsNotificationService, SmsNotificationService>();
        services.AddScoped<IApiOrderService, ApiOrderService>();

        return services;
    }
}
