using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioDependencies
{
    /// <summary>
    /// Registers the Twilio-backed SMS notification stack: settings bound from the
    /// <c>Twilio:</c> section, the messaging and lookup gateway clients (each as a typed
    /// <see cref="System.Net.Http.HttpClient"/>), and the order-notification orchestration.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ISmsGateway, TwilioMessagingClient>();

        // The lookup URL carries the number being validated in its path. Strip the default
        // HttpClient loggers so a shopper's number can never reach logs, whatever the log level.
        services.AddHttpClient<IPhoneNumberValidator, TwilioLookupClient>()
                .RemoveAllLoggers();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        // The new aggregate roots resolve through the open-generic EfRepository<> registration
        // already configured by the host (IRepository<> / IReadRepository<>).

        return services;
    }
}
