using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient(TwilioClient.MessagingClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddHttpClient(TwilioClient.LookupClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddSingleton<TwilioClient>();
        services.AddSingleton<ISmsGateway>(sp => sp.GetRequiredService<TwilioClient>());
        services.AddSingleton<IPhoneNumberLookup>(sp => sp.GetRequiredService<TwilioClient>());

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    public static ILoggingBuilder SuppressTwilioPhoneNumberHttpLogs(this ILoggingBuilder logging)
    {
        logging.AddFilter("System.Net.Http.HttpClient.TwilioLookup.ClientHandler", LogLevel.None);
        logging.AddFilter("System.Net.Http.HttpClient.TwilioLookup.LogicalHandler", LogLevel.None);
        logging.AddFilter("System.Net.Http.HttpClient.TwilioMessaging.LogicalHandler", LogLevel.Warning);
        logging.AddFilter("System.Net.Http.HttpClient.TwilioMessaging.ClientHandler", LogLevel.Warning);
        return logging;
    }
}
