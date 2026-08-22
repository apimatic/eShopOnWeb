using System;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddTransient<TwilioBasicAuthHandler>();

        services.AddHttpClient(TwilioMessagingClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddHttpClient(TwilioLookupClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://lookups.twilio.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddScoped<ITwilioMessagingClient, TwilioMessagingClient>();
        services.AddScoped<IPhoneNumberLookupClient, TwilioLookupClient>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    public static ILoggingBuilder SuppressTwilioHttpClientLogging(this ILoggingBuilder logging)
    {
        logging.AddFilter("System.Net.Http.HttpClient.TwilioMessaging", LogLevel.None);
        logging.AddFilter("System.Net.Http.HttpClient.TwilioLookup", LogLevel.None);
        return logging;
    }
}
