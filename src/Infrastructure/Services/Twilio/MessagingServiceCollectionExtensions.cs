using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS gateway and the order-notification / contact-number
    /// services. Binds the <c>Twilio:</c> configuration section (values come from user-secrets
    /// / environment, never hard-coded).
    /// </summary>
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        // The gateway owns a single long-lived HttpClient + SDK client for the app lifetime.
        services.AddSingleton<ISmsGateway, TwilioSmsGateway>();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
