using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the Twilio-backed SMS notification feature: settings bound from the <c>Twilio:</c>
/// configuration section, the hand-written provider clients, and the application services that
/// orchestrate them. Kept in PublicApi (a Web SDK project) so the HTTP-client and options-binding
/// extensions from the shared framework are available.
/// </summary>
public static class TwilioRegistration
{
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.ConfigSection));

        // RemoveAllLoggers() drops the default HttpClient request-logging, which would otherwise log
        // request URLs — and the lookup URL carries the shopper's phone number, which must never be logged.
        services.AddHttpClient<ISmsGateway, TwilioMessagingClient>()
            .RemoveAllLoggers();
        services.AddHttpClient<IPhoneNumberLookup, TwilioLookupClient>()
            .RemoveAllLoggers();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
