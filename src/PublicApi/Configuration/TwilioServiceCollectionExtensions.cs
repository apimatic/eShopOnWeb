using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Registers the SMS-notification feature: the Twilio HTTP clients and the application services
/// that drive contact numbers and order notifications.
/// </summary>
public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind the Twilio:* section (values come from user-secrets / env vars, never source).
        services.Configure<TwilioSettings>(configuration.GetSection("Twilio"));

        // Typed HTTP clients. RemoveAllLoggers() ensures request URLs — which carry destination
        // numbers — are never written to logs by the HTTP client factory.
        services.AddHttpClient<ITwilioMessagingClient, TwilioMessagingClient>()
            .RemoveAllLoggers();
        services.AddHttpClient<ITwilioLookupClient, TwilioLookupClient>()
            .RemoveAllLoggers();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
